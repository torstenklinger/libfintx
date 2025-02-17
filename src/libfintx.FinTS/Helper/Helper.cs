﻿/*	
 * 	
 *  This file is part of libfintx.
 *  
 *  Copyright (C) 2016 - 2021 Torsten Klinger
 * 	E-Mail: torsten.klinger@googlemail.com
 *  
 *  This program is free software; you can redistribute it and/or
 *  modify it under the terms of the GNU Lesser General Public
 *  License as published by the Free Software Foundation; either
 *  version 3 of the License, or (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
 *  Lesser General Public License for more details.
 *
 *  You should have received a copy of the GNU Lesser General Public License
 *  along with this program; if not, write to the Free Software Foundation,
 *  Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 * 	
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using libfintx.FinTS.Data.Segment;
using libfintx.Globals;
using libfintx.Logger.Log;

namespace libfintx.FinTS
{
    public static partial class Helper
    {
        /// <summary>
        /// Regex pattern for HIRMG/HIRMS messages.
        /// </summary>
        private const string PatternResultMessage = @"(\d{4}):.*?:(.+)";

        /// <summary>
        /// Combine byte arrays
        /// </summary>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        public static byte[] CombineByteArrays(byte[] first, byte[] second)
        {
            byte[] ret = new byte[first.Length + second.Length];

            Buffer.BlockCopy(first, 0, ret, 0, first.Length);
            Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);

            return ret;
        }

        /// <summary>
        /// Encode to Base64
        /// </summary>
        /// <param name="toEncode"></param>
        /// <returns></returns>
        public static string EncodeTo64(string toEncode)
        {
            byte[] toEncodeAsBytes = Encoding.ASCII.GetBytes(toEncode);
            string returnValue = Convert.ToBase64String(toEncodeAsBytes);

            return returnValue;
        }

        /// <summary>
        /// Decode from Base64
        /// </summary>
        /// <param name="encodedData"></param>
        /// <returns></returns>
        public static string DecodeFrom64(string encodedData)
        {
            byte[] encodedDataAsBytes = Convert.FromBase64String(encodedData);
            string returnValue = Encoding.ASCII.GetString(encodedDataAsBytes);

            return returnValue;
        }

        /// <summary>
        /// Decode from Base64 default
        /// </summary>
        /// <param name="encodedData"></param>
        /// <returns></returns>
        public static string DecodeFrom64EncodingDefault(string encodedData)
        {
            byte[] encodedDataAsBytes = Convert.FromBase64String(encodedData);
            string returnValue = Encoding.GetEncoding("ISO-8859-1").GetString(encodedDataAsBytes);

            return returnValue;
        }

        /// <summary>
        /// Encrypt -> HNVSD
        /// </summary>
        /// <param name="Segments"></param>
        /// <returns></returns>
        public static string Encrypt(string Segments)
        {
            return "HNVSD:999:1+@" + Segments.Length + "@" + Segments + "'";
        }

        /// <summary>
        /// Extract value from string
        /// </summary>
        /// <param name="StrSource"></param>
        /// <param name="StrStart"></param>
        /// <param name="StrEnd"></param>
        /// <returns></returns>
        public static string Parse_String(string StrSource, string StrStart, string StrEnd)
        {
            int Start, End;

            if (StrSource.Contains(StrStart) && StrSource.Contains(StrEnd))
            {
                Start = StrSource.IndexOf(StrStart, 0) + StrStart.Length;
                End = StrSource.IndexOf(StrEnd, Start);

                return StrSource.Substring(Start, End - Start);
            }
            else
            {
                return string.Empty;
            }
        }

        public static Segment Parse_Segment(string segmentCode)
        {
            Segment segment = null;
            try
            {
                segment = SegmentParserFactory.ParseSegment(segmentCode);
            }
            catch (Exception ex)
            {
                Log.Write($"Couldn't parse segment: {ex.Message}{Environment.NewLine}{segmentCode}");
            }
            return segment;
        }

        /// <summary>
        /// Parsing segment -> UPD, BPD
        /// </summary>
        /// <param name="UserID"></param>
        /// <param name="BLZ"></param>
        /// <param name="HBCIVersion"></param>
        /// <param name="Message"></param>
        /// <returns></returns>
        public static List<HBCIBankMessage> Parse_Segments(FinTsClient client, string Message)
        {
            try
            {
                var connDetails = client.ConnectionDetails;
                List<HBCIBankMessage> result = new List<HBCIBankMessage>();

                List<string> rawSegments = SplitEncryptedSegments(Message);

                List<Segment> segments = new List<Segment>();
                foreach (var item in rawSegments)
                {
                    Segment segment = Parse_Segment(item);
                    if (segment != null)
                        segments.Add(segment);
                }

                // BPD
                string bpd = string.Empty;
                var bpaMatch = Regex.Match(Message, @"(HIBPA.+?)\b(HITAN|HNHBS|HISYN|HIUPA)\b");
                if (bpaMatch.Success)
                    bpd = bpaMatch.Groups[1].Value;
                if (bpd.Length > 0)
                {
                    SaveBPD(connDetails.Blz, bpd);
                    BPD.ParseBpd(bpd);
                }

                // UPD
                string upd = string.Empty;
                var upaMatch = Regex.Match(Message, @"(HIUPA.+?)\b(HITAN|HNHBS)\b");
                if (upaMatch.Success)
                    upd = upaMatch.Groups[1].Value;
                if (upd.Length > 0)
                {
                    SaveUPD(connDetails.Blz, connDetails.UserId, upd);
                    UPD.ParseUpd(upd);
                }

                if (UPD.AccountList != null)
                {
                    //Add BIC to Account information (Not retrieved bz UPD??)
                    foreach (AccountInformation accInfo in UPD.AccountList)
                        accInfo.AccountBic = connDetails.Bic;
                }

                foreach (var segment in segments)
                {
                    if (segment.Name == "HIRMG")
                    {
                        // HIRMG:2:2+9050::Die Nachricht enthÃ¤lt Fehler.+9800::Dialog abgebrochen+9010::Initialisierung fehlgeschlagen, Auftrag nicht bearbeitet.
                        // HIRMG:2:2+9800::Dialogabbruch.

                        string[] HIRMG_messages = segment.Payload.Split('+');
                        foreach (var HIRMG_message in HIRMG_messages)
                        {
                            var message = Parse_BankCode_Message(HIRMG_message);
                            if (message != null)
                                result.Add(message);
                        }
                    }

                    if (segment.Name == "HIRMS")
                    {
                        // HIRMS:3:2:2+9942::PIN falsch. Zugang gesperrt.'
                        string[] HIRMS_messages = segment.Payload.Split('+');
                        foreach (var HIRMS_message in HIRMS_messages)
                        {
                            var message = Parse_BankCode_Message(HIRMS_message);
                            if (message != null)
                                result.Add(message);
                        }

                        var securityMessage = result.FirstOrDefault(m => m.Code == "3920");
                        if (securityMessage != null)
                        {
                            string message = securityMessage.Message;

                            string TAN = string.Empty;
                            string TANf = string.Empty;

                            string[] procedures = Regex.Split(message, @"\D+");

                            foreach (string value in procedures)
                            {
                                if (!string.IsNullOrEmpty(value) && int.TryParse(value, out int i))
                                {
                                    if (Convert.ToString(i).StartsWith("9"))
                                    {
                                        if (string.IsNullOrEmpty(TAN))
                                            TAN = i.ToString();

                                        if (string.IsNullOrEmpty(TANf))
                                            TANf = i.ToString();
                                        else
                                            TANf += $";{i}";
                                    }
                                }
                            }
                            if (string.IsNullOrEmpty(client.HIRMS))
                            {
                                client.HIRMS = TAN;
                            }
                            else
                            {
                                if (!TANf.Contains(client.HIRMS))
                                    throw new Exception($"Invalid HIRMS/Tan-Mode {client.HIRMS} detected. Please choose one of the allowed modes: {TANf}");
                            }
                            client.HIRMSf = TANf;

                            // Parsing TAN processes
                            if (!string.IsNullOrEmpty(client.HIRMS))
                                Parse_TANProcesses(client, bpd);

                        }
                    }

                    if (segment.Name == "HNHBK")
                    {
                        var ID = Parse_String(segment.Payload, "+1+", ":1");
                        client.HNHBK = ID;
                    }

                    if (segment.Name == "HISYN")
                    {
                        client.SystemId = segment.Payload;
                        Log.Write("Customer System ID: " + client.SystemId);
                    }

                    if (segment.Name == "HNHBS")
                    {
                        if (segment.Payload == null || segment.Payload == "0")
                            client.HNHBS = 2;
                        else
                            client.HNHBS = Convert.ToInt32(segment.Payload) + 1;
                    }

                    if (segment.Name == "HISALS")
                    {
                        client.HISALS = segment.Version;
                    }

                    if (segment.Name == "HITANS")
                    {
                        if (client.HITANS != 0 && segment.Version == 7) // Torsten: Rücknahme der Freigabe. Sparkassen ausserhalb der
                            // Pilotphase senden in der BPD bereits das Segment, welches in der UPD noch nicht zur Verfügung steht und somit
                            // zu Abbrüchen führt.
                        //if (client.HITANS != 0) // Torsten: Freigabe HKTAN#7 in libfintx
                            ; // Ignore HKTAN version 7 if other version is available and version 7 isn't implemented in libfintx
                        else
                            client.HITANS = segment.Version;
                    }

                    if (segment.Name == "HITAN")
                    {
                        client.HITAN = Parse_String(segment.Payload.Replace("?+", "??"), "++", "+").Replace("??", "?+");
                    }

                    if (segment.Name == "HIKAZS")
                    {
                        if (client.HIKAZS == 0)
                        {
                            client.HIKAZS = segment.Version;
                        }
                        else
                        {
                            if (segment.Version > client.HIKAZS)
                                client.HIKAZS = segment.Version;
                        }
                    }

                    if (segment.Name == "HISPAS")
                    {
                        if (segment.Payload.Contains("pain.001.001.03"))
                            client.HISPAS = 1;
                        else if (segment.Payload.Contains("pain.001.002.03"))
                            client.HISPAS = 2;
                        else if (segment.Payload.Contains("pain.001.003.03"))
                            client.HISPAS = 3;

                        if (client.HISPAS == 0)
                            client.HISPAS = 3; // -> Fallback. Most banks accept the newest pain version
                    }
                }

                // Fallback if HIKAZS is not delivered by BPD (eg. Postbank)
                if (client.HIKAZS == 0)
                    client.HIKAZS = 0;

                return result;
            }
            catch (Exception ex)
            {
                Log.Write(ex.ToString());

                throw new InvalidOperationException($"Software error.", ex);
            }
        }

        /// <summary>
        /// Parsing bank message
        /// </summary>
        /// <param name="Message"></param>
        /// <returns></returns>
        public static bool Parse_Message(FinTsClient client, string Message)
        {
            try
            {
                List<string> values = SplitEncryptedSegments(Message);

                List<Segment> segments = new List<Segment>();
                foreach (var item in values)
                {
                    Segment segment = Parse_Segment(item);
                    if (segment != null)
                        segments.Add(segment);
                }

                foreach (var segment in segments)
                {
                    if (segment.Name == "HNHBS")
                    {
                        if (segment.Payload == null || segment.Payload == "0")
                            client.HNHBS = 2;
                        else
                            client.HNHBS = Convert.ToInt32(segment.Payload) + 1;
                    }

                    if (segment.Name == "HITAN")
                    {
                        client.HITAN = Parse_String(segment.Payload.Replace("?+", "??"), "++", "+").Replace("??", "?+");
                    }
                }

                return client.HNHBS > 0;
            }
            catch (Exception ex)
            {
                Log.Write(ex.ToString());

                return false;
            }
        }

        /// <summary>
        /// Parse balance
        /// </summary>
        /// <param name="Message"></param>
        /// <returns></returns>
        public static AccountBalance Parse_Balance(string Message)
        {
            var hirms = Message.Substring(Message.IndexOf("HIRMS") + 5);
            hirms = hirms.Substring(0, (hirms.Contains("'") ? hirms.IndexOf('\'') : hirms.Length));
            var hirmsParts = hirms.Split(':');

            AccountBalance balance = new AccountBalance();
            balance.Message = hirmsParts[hirmsParts.Length - 1];

            if (Message.Contains("+0020::"))
            {
                var hisal = Message.Substring(Message.IndexOf("HISAL") + 5);
                hisal = hisal.Substring(0, (hisal.Contains("'") ? hisal.IndexOf('\'') : hisal.Length));
                var hisalParts = hisal.Split('+');

                balance.Successful = true;

                var hisalAccountParts = hisalParts[1].Split(':');
                if (hisalAccountParts.Length == 4)
                {
                    balance.AccountType = new AccountInformation()
                    {
                        AccountNumber = hisalAccountParts[0],
                        AccountBankCode = hisalAccountParts.Length > 3 ? hisalAccountParts[3] : null,
                        AccountType = hisalParts[2],
                        AccountCurrency = hisalParts[3],
                        AccountBic = !string.IsNullOrEmpty(hisalAccountParts[1]) ? hisalAccountParts[1] : null
                    };
                }
                else if (hisalAccountParts.Length == 2)
                {
                    balance.AccountType = new AccountInformation()
                    {
                        AccountIban = hisalAccountParts[0],
                        AccountBic = hisalAccountParts[1]
                    };
                }

                var hisalBalanceParts = hisalParts[4].Split(':');
                if (hisalBalanceParts[1].IndexOf("e-9", StringComparison.OrdinalIgnoreCase) >= 0)
                    balance.Balance = 0; // Deutsche Bank liefert manchmal "E-9", wenn der Kontostand 0 ist. Siehe Test_Parse_Balance und https://homebanking-hilfe.de/forum/topic.php?t=24155
                else
                    balance.Balance = Convert.ToDecimal($"{(hisalBalanceParts[0] == "D" ? "-" : "")}{hisalBalanceParts[1]}");


                //from here on optional fields / see page 46 in "FinTS_3.0_Messages_Geschaeftsvorfaelle_2015-08-07_final_version.pdf"
                if (hisalParts.Length > 5 && hisalParts[5].Contains(":"))
                {
                    var hisalMarkedBalanceParts = hisalParts[5].Split(':');
                    balance.MarkedTransactions = Convert.ToDecimal($"{(hisalMarkedBalanceParts[0] == "D" ? "-" : "")}{hisalMarkedBalanceParts[1]}");
                }

                if (hisalParts.Length > 6 && hisalParts[6].Contains(":"))
                {
                    balance.CreditLine = Convert.ToDecimal(hisalParts[6].Split(':')[0].TrimEnd(','));
                }

                if (hisalParts.Length > 7 && hisalParts[7].Contains(":"))
                {
                    balance.AvailableBalance = Convert.ToDecimal(hisalParts[7].Split(':')[0].TrimEnd(','));
                }

                /* ---------------------------------------------------------------------------------------------------------
                 * In addition to the above fields, the following fields from HISAL could also be implemented:
                 * 
                 * - 9/Bereits verfügter Betrag
                 * - 10/Überziehung
                 * - 11/Buchungszeitpunkt
                 * - 12/Fälligkeit 
                 * 
                 * Unfortunately I'm missing test samples. So I drop support unless we get test messages for this fields.
                 ------------------------------------------------------------------------------------------------------------ */
            }
            else
            {
                balance.Successful = false;

                string msg = string.Empty;
                for (int i = 1; i < hirmsParts.Length; i++)
                {
                    msg = msg + "??" + hirmsParts[i].Replace("::", ": ");
                }
                Log.Write(msg);
            }

            return balance;
        }

        /// <summary>
        /// Parse tan processes
        /// </summary>
        /// <returns></returns>
        private static bool Parse_TANProcesses(FinTsClient client, string bpd)
        {
            try
            {
                List<TanProcess> list = new List<TanProcess>();

                string[] processes = client.HIRMSf.Split(';');

                // Examples from bpd

                // 944:2:SECUREGO:
                // 920:2:smsTAN:
                // 920:2:BestSign:

                foreach (var process in processes)
                {
                    string pattern = process + ":.*?:.*?:(?'name'.*?):.*?:(?'name2'.*?):";

                    Regex rgx = new Regex(pattern);

                    foreach (Match match in rgx.Matches(bpd))
                    {
                        int i = 0;

                        if (!process.Equals("999")) // -> PIN/TAN step 1
                        {
                            if (int.TryParse(match.Groups["name2"].Value, out i))
                                list.Add(new TanProcess { ProcessNumber = process, ProcessName = match.Groups["name"].Value });
                            else
                                list.Add(new TanProcess { ProcessNumber = process, ProcessName = match.Groups["name2"].Value });
                        }
                    }
                }

                TanProcesses.Items = list;

                return true;
            }
            catch { return false; }
        }

        public static List<string> Parse_TANMedium(string BankCode)
        {
            List<string> result = new List<string>();

            // HITAB:5:4:3+0+A:1:::::::::::Handy::::::::+A:2:::::::::::iPhone Abid::::::::
            // HITAB:4:4:3+0+M:1:::::::::::mT?:MFN1:********0340'
            // HITAB:5:4:3+0+M:2:::::::::::Unregistriert 1::01514/654321::::::+M:1:::::::::::Handy:*********4321:::::::
            // HITAB:4:4:3+0+M:1:::::::::::mT?:MFN1:********0340+G:1:SO?:iPhone:00:::::::::SO?:iPhone''

            // For easier matching, replace '?:' by some special character
            BankCode = BankCode.Replace("?:", @"\");

            foreach (Match match in Regex.Matches(BankCode, @"\+[AGMS]:[012]:(?<Kartennummer>[^:]*):(?<Kartenfolgenummer>[^:]*):+(?<Bezeichnung>[^+:]+)"))
            {
                result.Add(match.Groups["Bezeichnung"].Value.Replace(@"\", "?:"));
            }

            return result;
        }

        private static FlickerRenderer flickerCodeRenderer = null;

        /// <summary>
        /// Fill given <code>TANDialog</code> and wait for user to enter a TAN.
        /// </summary>
        /// <param name="BankCode"></param>
        /// <param name="pictureBox"></param>
        /// <param name="flickerImage"></param>
        /// <param name="flickerWidth"></param>
        /// <param name="flickerHeight"></param>
        /// <param name="renderFlickerCodeAsGif"></param>
        public static async Task<string> WaitForTanAsync(FinTsClient client, HBCIDialogResult dialogResult, TANDialog tanDialog)
        {
            var BankCode_ = "HIRMS" + Parse_String(dialogResult.RawData, "'HIRMS", "'");
            string[] values = BankCode_.Split('+');
            foreach (var item in values)
            {
                if (!item.StartsWith("HIRMS"))
                    TransactionConsole.Output = item.Replace("::", ": ");
            }

            var HITAN = "HITAN" + Parse_String(dialogResult.RawData.Replace("?'", "").Replace("?:", ":").Replace("<br>", Environment.NewLine).Replace("?+", "??"), "'HITAN", "'");

            string HITANFlicker = string.Empty;

            var processes = TanProcesses.Items;

            var processname = string.Empty;

            if (processes != null)
            {
                foreach (var item in processes)
                {
                    if (item.ProcessNumber.Equals(client.HIRMS))
                        processname = item.ProcessName;
                }
            }

            // Smart-TAN plus optisch
            // chipTAN optisch
            if (processname.Equals("Smart-TAN plus optisch") || processname.Contains("chipTAN optisch"))
            {
                HITANFlicker = HITAN;
            }

            String[] values_ = HITAN.Split('+');

            int i = 1;

            foreach (var item in values_)
            {
                i = i + 1;

                if (i == 6)
                {
                    TransactionConsole.Output = TransactionConsole.Output + "??" + item.Replace("::", ": ").TrimStart();

                    TransactionConsole.Output = TransactionConsole.Output.Replace("??", " ")
                            .Replace("0030: ", "")
                            .Replace("1.", Environment.NewLine + "1.")
                            .Replace("2.", Environment.NewLine + "2.")
                            .Replace("3.", Environment.NewLine + "3.")
                            .Replace("4.", Environment.NewLine + "4.")
                            .Replace("5.", Environment.NewLine + "5.")
                            .Replace("6.", Environment.NewLine + "6.")
                            .Replace("7.", Environment.NewLine + "7.")
                            .Replace("8.", Environment.NewLine + "8.");
                }
            }

            // chipTAN optisch
            if (processname.Contains("chipTAN optisch"))
            {
                string FlickerCode = string.Empty;

                FlickerCode = "CHLGUC" + Helper.Parse_String(HITAN, "CHLGUC", "CHLGTEXT") + "CHLGTEXT";

                FlickerCode flickerCode = new FlickerCode(FlickerCode);
                flickerCodeRenderer = new FlickerRenderer(flickerCode.Render(), tanDialog.PictureBox);
                if (!tanDialog.RenderFlickerCodeAsGif)
                {
                    RUN_flickerCodeRenderer();

                    Action action = STOP_flickerCodeRenderer;
                    TimeSpan span = new TimeSpan(0, 0, 0, 50);

                    ThreadStart start = delegate { RunAfterTimespan(action, span); };
                    Thread thread = new Thread(start);
                    thread.Start();
                }
                else
                {
                    tanDialog.FlickerImage = flickerCodeRenderer.RenderAsGif(tanDialog.FlickerWidth, tanDialog.FlickerHeight);
                }
            }

            // Smart-TAN plus optisch
            if (processname.Equals("Smart-TAN plus optisch"))
            {
                HITANFlicker = HITAN.Replace("?@", "??");

                string FlickerCode = string.Empty;

                String[] values__ = HITANFlicker.Split('@');

                int ii = 1;

                foreach (var item in values__)
                {
                    ii = ii + 1;

                    if (ii == 4)
                        FlickerCode = item;
                }

                FlickerCode flickerCode = new FlickerCode(FlickerCode.Trim());
                flickerCodeRenderer = new FlickerRenderer(flickerCode.Render(), tanDialog.PictureBox);
                if (!tanDialog.RenderFlickerCodeAsGif)
                {
                    RUN_flickerCodeRenderer();

                    Action action = STOP_flickerCodeRenderer;
                    TimeSpan span = new TimeSpan(0, 0, 0, 50);

                    ThreadStart start = delegate { RunAfterTimespan(action, span); };
                    Thread thread = new Thread(start);
                    thread.Start();
                }
                else
                {
                    tanDialog.FlickerImage = flickerCodeRenderer.RenderAsGif(tanDialog.FlickerWidth, tanDialog.FlickerHeight);
                }
            }

            // Smart-TAN photo
            if (processname.Equals("Smart-TAN photo"))
            {
                var PhotoCode = Parse_String(dialogResult.RawData, ".+@", "'HNSHA");

                var mCode = new MatrixCode(PhotoCode.Substring(5, PhotoCode.Length - 5));

                tanDialog.MatrixImage = mCode.CodeImage;
                mCode.Render(tanDialog.PictureBox);
            }

            // PhotoTAN
            if (processname.Equals("photoTAN-Verfahren"))
            {
                // HITAN:5:5:4+4++nmf3VmGQDT4qZ20190130091914641+Bitte geben Sie die photoTan ein+@3031@       image/pngÃŠÂ‰PNG
                var match = Regex.Match(dialogResult.RawData, @"HITAN.+@\d+@(.+)'HNHBS", RegexOptions.Singleline);
                if (match.Success)
                {
                    var PhotoBinary = match.Groups[1].Value;

                    var mCode = new MatrixCode(PhotoBinary);

                    tanDialog.MatrixImage = mCode.CodeImage;
                    mCode.Render(tanDialog.PictureBox);
                }
            }

            return await tanDialog.WaitForTanAsync();
        }

        /// <summary>
        /// Parse a single bank result message.
        /// </summary>
        /// <param name="BankCodeMessage"></param>
        /// <returns></returns>
        public static HBCIBankMessage Parse_BankCode_Message(string BankCodeMessage)
        {
            var match = Regex.Match(BankCodeMessage, PatternResultMessage);
            if (match.Success)
            {
                var code = match.Groups[1].Value;
                var message = match.Groups[2].Value;

                message = message.Replace("?:", ":");

                return new HBCIBankMessage(code, message);
            }
            return null;
        }

        /// <summary>
        /// Parse bank error codes
        /// </summary>
        /// <param name="BankCode"></param>
        /// <returns>Banks messages with "??" as seperator.</returns>
        public static List<HBCIBankMessage> Parse_BankCode(string BankCode)
        {
            List<HBCIBankMessage> result = new List<HBCIBankMessage>();

            string[] segments = BankCode.Split('\'');
            foreach (var segment in segments)
            {
                if (segment.Contains("HIRMG") || segment.Contains("HIRMS"))
                {
                    string[] messages = segment.Split('+');
                    foreach (var HIRMG_message in messages)
                    {
                        var message = Parse_BankCode_Message(HIRMG_message);
                        if (message != null)
                            result.Add(message);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// RUN Flicker Code Rendering
        /// </summary>
        private static void RUN_flickerCodeRenderer()
        {
            flickerCodeRenderer.Start();
        }

        /// <summary>
        /// STOP Flicker Code Rendering
        /// </summary>
        public static void RunAfterTimespan(Action action, TimeSpan span)
        {
            Thread.Sleep(span);
            action();
        }

        private static void STOP_flickerCodeRenderer()
        {
            flickerCodeRenderer.Stop();
        }

        /// <summary>
        /// Make filename valid
        /// </summary>
        public static string MakeFilenameValid(string value)
        {
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }
            return value.Replace(" ", "_");
        }

        private static string GetBPDDir()
        {
            var dir = FinTsGlobals.ProgramBaseDir;
            return Path.Combine(dir, "BPD");
        }

        private static string GetBPDFile(string dir, int BLZ)
        {
            return Path.Combine(dir, "280_" + BLZ + ".bpd");
        }

        private static string GetUPDDir()
        {
            var dir = FinTsGlobals.ProgramBaseDir;
            return Path.Combine(dir, "UPD");
        }

        private static string GetUPDFile(string dir, int BLZ, string UserID)
        {
            return Path.Combine(dir, "280_" + BLZ + "_" + UserID + ".upd");
        }

        public static void SaveUPD(int BLZ, string UserID, string upd)
        {
            string dir = GetUPDDir();
            Directory.CreateDirectory(dir);
            var file = GetUPDFile(dir, BLZ, UserID);
            if (!File.Exists(file))
            {
                using (File.Create(file)) { };
            }
            File.WriteAllText(file, upd);
        }

        public static string GetUPD(int BLZ, string UserID)
        {
            var dir = GetUPDDir();
            var file = GetUPDFile(dir, BLZ, UserID);
            var content = File.Exists(file) ? File.ReadAllText(file) : string.Empty;

            return content;
        }

        public static void SaveBPD(int BLZ, string upd)
        {
            string dir = GetBPDDir();
            Directory.CreateDirectory(dir);
            var file = GetBPDFile(dir, BLZ);
            if (!File.Exists(file))
            {
                using (File.Create(file)) { };
            }
            File.WriteAllText(file, upd);
        }

        public static string GetBPD(int BLZ)
        {
            var dir = GetBPDDir();
            var file = GetBPDFile(dir, BLZ);
            var content = File.Exists(file) ? File.ReadAllText(file) : string.Empty;

            return content;
        }

        public static bool IsTANRequired(string gvName)
        {
            var HIPINS = BPD.HIPINS;
            return HIPINS != null && HIPINS.IsTanRequired(gvName);
        }

    }
}


