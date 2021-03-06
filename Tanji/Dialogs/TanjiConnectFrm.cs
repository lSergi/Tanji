﻿/* Copyright

    GitHub(Source): https://GitHub.com/ArachisH/Tanji

    .NET library for creating Habbo Hotel related desktop applications.
    Copyright (C) 2015 ArachisH

    This program is free software; you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation; either version 2 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License along
    with this program; if not, write to the Free Software Foundation, Inc.,
    51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.

    See License.txt in the project root for license information.
*/

using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Collections.Generic;

using Tanji.Managers;

using Sulakore;
using Sulakore.Habbo.Web;
using Sulakore.Communication;

using Eavesdrop;

using FlashInspect;
using FlashInspect.Tags;

namespace Tanji.Dialogs
{
    public partial class TanjiConnectFrm : Form
    {
        private readonly TaskScheduler _uiContext;

        public MainFrm Main { get; private set; }
        public bool IsConnecting { get; private set; }

        public TanjiConnectFrm(MainFrm main)
        {
            InitializeComponent();
            Main = main;

            _uiContext =
                TaskScheduler.FromCurrentSynchronizationContext();

            if (!Directory.Exists("Modified Clients"))
                Directory.CreateDirectory("Modified Clients");

            CreateTrustedRootCertificate();
            Eavesdropper.IsSslSupported = true;
            Eavesdropper.EavesdropperResponse += EavesdropperResponse;
        }

        private async void BrowseBtn_Click(object sender, EventArgs e)
        {
            ChooseClientDlg.FileName = ChooseClientDlg.SafeFileName;
            if (ChooseClientDlg.ShowDialog() != DialogResult.OK) return;

            bool verifySuccess = await VerifyGameClientAsync(
                ChooseClientDlg.FileName);

            StatusTxt.StopDotAnimation("Standing By...");
            if (!verifySuccess)
            {
                MessageBox.Show("Unable to dissassemble the Shockwave Flash(.swf) file.",
                    "Tanji ~ Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            CustomClientTxt.Text = verifySuccess ?
                Main.Game.Location : string.Empty;
        }
        private async void ConnectBtn_Click(object sender, EventArgs e)
        {
            IsConnecting = !IsConnecting;
            if (IsConnecting)
            {
                ModePnl.Enabled = !(GameHostTxt.ReadOnly =
                    GamePortTxt.ReadOnly = ExponentTxt.ReadOnly = ModulusTxt.ReadOnly = true);

                ConnectBtn.Text = "Cancel";
                if (ModePnl.IsManual)
                {
                    if (!(await DoManualConnectAsync()))
                    {
                        ResetInterface();
                        MessageBox.Show("Something went wrong when attempting to intercept the connection.",
                            "Tanji ~ Error!",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                    else Close();
                }
                else DoAutomaticConnect();
            }
            else DoCancelConnect();
        }
        private void TanjiConnectFrm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                ResetInterface();
                Eavesdropper.Terminate();
                HConnection.RestoreHosts();
            }
            finally
            {
                if (!Main.Connection.IsConnected)
                    Environment.Exit(0);
            }
        }

        private void ModeChanged(object sender, EventArgs e)
        {
            GameHostTxt.ReadOnly =
                GamePortTxt.ReadOnly = !ModePnl.IsManual;

            BrowseBtn.Enabled = !ModePnl.IsManual;

            Text = string.Format("Tanji ~ Connection Setup [{0}]",
                ModePnl.IsManual ? "Manual" : "Automatic");
        }
        private void EavesdropperResponse(object sender, EavesdropperResponseEventArgs e)
        {
            if (e.Response.ContentType == "application/x-shockwave-flash" && e.Payload.Length > 3000000)
            {
                if (Main.Game == null)
                {
                    bool verifySuccess = VerifyGameClientAsync(
                        new ShockwaveFlash(e.Payload)).Result;

                    if (verifySuccess)
                    {
                        StatusTxt.SetDotAnimation("Extracting Tags");
                        var binaryDataTags = Main.Game.ExtractTags()
                            .OfType<DefineBinaryDataTag>();

                        ReplaceRsaKeys(binaryDataTags);
                        e.Payload = Main.Game.Rebuild();
                    }
                    Main.Game.Save("Modified Clients\\" + Main.GameData.FlashClientBuild + ".swf");
                }
                else e.Payload = Main.Game.Data;

                Eavesdropper.Terminate();
                StatusTxt.SetDotAnimation("Intercepting Connection");

                Main.Connection.ConnectAsync(Main.GameData.Host, Main.GameData.Port)
                    .ContinueWith(t => Close(), _uiContext);
            }
            else
            {
                string responseBody = Encoding.UTF8.GetString(e.Payload);
                if (responseBody.Contains("connection.info.host"))
                {
                    Main.GameData = new HGameData(responseBody);
                    Main.Contractor.Hotel = SKore.ToHotel(Main.GameData.Host);
                    Main.IsRetro = (Main.Contractor.Hotel == HHotel.Unknown);

                    if (Main.Game == null && !Main.IsRetro)
                        TryLoadModdedClientAsync().Wait();

                    if (Main.Game == null && Main.IsRetro)
                    {
                        Eavesdropper.Terminate();
                        StatusTxt.SetDotAnimation("Intercepting Connection");

                        Main.Connection.ConnectAsync(Main.GameData.Host, Main.GameData.Port)
                            .ContinueWith(t => Close(), _uiContext);
                    }
                    else
                    {
                        StatusTxt.SetDotAnimation((Main.GameData == null ?
                            "Intercepting" : "Replacing") + " Client");
                    }

                    responseBody = responseBody.Replace(".swf", ".swf?" + DateTime.Now.Millisecond);
                    e.Payload = Encoding.UTF8.GetBytes(responseBody);
                }
            }
        }

        private void ExtractRsaKeys(string base64RsaKeys)
        {
            byte[] rsaKeyData = Convert.FromBase64String(base64RsaKeys);
            string mergedRsaKeys = Encoding.UTF8.GetString(rsaKeyData);

            int modLength = mergedRsaKeys[0];
            string modulus = mergedRsaKeys.Substring(1, modLength);

            mergedRsaKeys = mergedRsaKeys.Substring(modLength);
            int exponent = int.Parse(mergedRsaKeys.Substring(2));

            if (string.IsNullOrWhiteSpace(Main.Handshaker.RealModulus))
                Main.Handshaker.RealModulus = modulus;

            if (Main.Handshaker.RealExponent == 0)
                Main.Handshaker.RealExponent = exponent;
        }
        private string EncodeRsaKeys(int exponent, string modulus)
        {
            string mergedKeys = string.Format("{0}{1} {2}",
                (char)modulus.Length, modulus, exponent);

            byte[] data = Encoding.UTF8.GetBytes(mergedKeys);
            return Convert.ToBase64String(data);
        }
        private void ReplaceRsaKeys(IEnumerable<DefineBinaryDataTag> binaryDataTags)
        {
            foreach (DefineBinaryDataTag binaryDataTag in binaryDataTags)
            {
                string binaryDataBody = Encoding.UTF8
                    .GetString(binaryDataTag.BinaryData);

                if (binaryDataBody.Contains("habbo_login_dialog"))
                {
                    string realRsaKeys = binaryDataBody
                        .GetChild("name=\"dummy_field\" caption=\"", '"');

                    ExtractRsaKeys(realRsaKeys);

                    string fakeRsaKeys = EncodeRsaKeys(
                        HandshakeManager.FAKE_EXPONENT, HandshakeManager.FAKE_MODULUS);

                    binaryDataTag.BinaryData = Encoding.UTF8.GetBytes(
                        binaryDataBody.Replace(realRsaKeys, fakeRsaKeys));

                    break;
                }
            }
        }

        private void DoCancelConnect()
        {
            ResetInterface();
            Eavesdropper.Terminate();
            Main.Connection.Disconnect();
        }
        private void DoAutomaticConnect()
        {
            Eavesdropper.Initiate(8080);
            StatusTxt.SetDotAnimation("Extracting Host/Port");
        }
        private async Task<bool> DoManualConnectAsync()
        {
            StatusTxt.SetDotAnimation("Intercepting Connection");

            await Main.Connection.ConnectAsync(
                GameHostTxt.Text, int.Parse(GamePortTxt.Text));

            return Main.Connection.IsConnected;
        }

        private void ResetInterface()
        {
            IsConnecting = false;
            ConnectBtn.Text = "Connect";
            StatusTxt.StopDotAnimation("Standing By...");
            GameHostTxt.ReadOnly = GamePortTxt.ReadOnly = !ModePnl.IsManual;
            ModePnl.Enabled = !(ExponentTxt.ReadOnly = ModulusTxt.ReadOnly = false);
        }
        private void CreateTrustedRootCertificate()
        {
            while (!Eavesdropper.CreateTrustedRootCertificate())
            {
                var result = MessageBox.Show(
                    "Eavesdrop requires a self-signed certificate in the root store to decrypt HTTPS traffic.\r\n\r\nWould you like to retry the process?\r\n\r\n",
                    "Tanji ~ Alert!", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk);

                if (result == DialogResult.No)
                    Environment.Exit(0);
            }
        }
        private async Task<bool> TryLoadModdedClientAsync()
        {
            string possibleClientPath = Path.Combine("Modified Clients",
               Main.GameData.FlashClientBuild + ".swf");

            bool loadSuccess = false;
            if (File.Exists(possibleClientPath))
            {
                loadSuccess = await VerifyGameClientAsync(
                    possibleClientPath).ConfigureAwait(false);

                if (!loadSuccess)
                    File.Delete(possibleClientPath);
            }
            return loadSuccess;
        }

        private Task<bool> VerifyGameClientAsync(string path)
        {
            return VerifyGameClientAsync(new ShockwaveFlash(path));
        }
        private async Task<bool> VerifyGameClientAsync(ShockwaveFlash flash)
        {
            if (flash.IsCompressed)
            {
                StatusTxt.SetDotAnimation("Decompressing Client");
                bool decompressed = await flash.DecompressAsync()
                    .ConfigureAwait(false);
            }

            if (!flash.IsCompressed)
                Main.Game = flash;

            return Main.Game != null;
        }
    }
}