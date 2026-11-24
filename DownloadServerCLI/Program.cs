/*
 * Copyright (c) 2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of DownloadServerCLI <https://github.com/OpenChargingCloud/DownloadServerCLI>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.CommandLine;
using cloud.charging.open.protocols.WWCP.Node.Configuration;

using cloud.charging.open.DownloadServer.CommandLine;

#endregion

namespace cloud.charging.open.DownloadServer
{

    /// <summary>
    /// One download server for charging stations and local controllers, with
    /// a prompt, until 'quit', Ctrl+C or SIGTERM.
    /// </summary>
    /// <remarks>
    /// What every kind of node's program does is the node's: the switches and
    /// the words -h explains them with, why it could not be set up or could not
    /// start, what goes into the certificate store, the banner and the prompt.
    /// What is left here is the download server's: the switches that say for
    /// one start what the "downloadServer" section says for every start, and
    /// what the banner says of its files and their downloads.
    /// </remarks>
    public class Program
    {

        #region (private static) Usage

        /// <summary>
        /// What -h shows: every node's switches, in a download server's words,
        /// and the download server's own.
        /// </summary>
        private static readonly NodeUsage Usage = new (

            Program:               "DownloadServerCLI",
            Kind:                  DownloadServerNode.DownloadServerKind,
            DefaultPort:           DownloadServerNode.DefaultHTTPPort,
            FrontendSources:       "libs/DownloadServer/DownloadServer/Frontend",
            ConfigurationSays:     "where the files, the name servers and the groups of time servers of this download server live " +
                                  $"(default: {WWCPConfigFile.DefaultFileName} below the repository root). Without the file it offers " +
                                   "the files of the area 'firmware' by HTTP GET and over SFTP, from 'files/firmware' beside it; the " +
                                   "\"downloadServer\" section says otherwise - see the README.",
            CertificateKinds:      DownloadServerNode.CertificateKinds,
            Synopsis:              [ "[--download-port <n>]", "[--download-listen <address>]", "[--files <directory>]", "[--areas <area>[,<area>...]]",
                                     "[--no-http-downloads | --no-sftp-downloads]", "[--anonymous-downloads]" ],
            AfterTheWebInterface: [
                .. NodeUsage.Wrap("The files and their downloads - each of these says for this start what the \"downloadServer\" " +
                                  "section of the configuration file says for every start:",
                                  "",
                                  ""),
                .. NodeUsage.Switch("--download-port <n>",          $"the TCP port files are offered on by HTTP GET (default: {DownloadServerSettings.DefaultHTTPDownloadPort}). " +
                                                                    "A port of its own, so that charging stations can reach it while the web interface stays " +
                                                                    "on the loopback."),
                .. NodeUsage.Switch("--download-listen <address>",  "the address files are offered on by HTTP GET (default: \"any\", every address of both " +
                                                                    "families). Not --any, which is the web interface's - and SSH's, and so SFTP's."),
                .. NodeUsage.Switch("--files <directory>",          $"where the areas and the record of the downloads lie (default: \"{DownloadServerSettings.DefaultDirectory}\" " +
                                                                    "beside the configuration file)."),
                .. NodeUsage.Switch("--areas <area>[,<area>...]",   $"the areas files are offered in, one directory each below the files and the first part of " +
                                                                    $"the path a file is fetched by (default: \"{String.Join(",", DownloadServerSettings.DefaultAreas)}\"). " +
                                                                    "Lower-case letters, digits, '-' and '_'."),
                .. NodeUsage.Switch("--no-http-downloads",          "offer nothing by HTTP GET."),
                .. NodeUsage.Switch("--no-sftp-downloads",          "offer nothing over SFTP."),
                .. NodeUsage.Switch("--anonymous-downloads",        "serve HTTP downloads without credentials as well - to whoever can reach the port."),
                ""
            ]

        );

        #endregion

        #region (private static) WhatToDoAbout(Problem) / SwitchOf(Problem)

        /// <summary>
        /// The way past the download port, in the words of whoever started it;
        /// null for the web interface's, which is every node's.
        /// </summary>
        private static String? WhatToDoAbout(PortUnavailableException Problem)

            => Problem.Whose == DownloadServerNode.HTTPDownloadPort
                   ? "Stop whatever has it - another download server, a web server - or give HTTP downloads another port with " +
                     "--download-port <number>, or one address with --download-listen <address>, or none with --no-http-downloads."
                   : null;

        private static String? SwitchOf(PortUnavailableException Problem)

            => Problem.Whose == DownloadServerNode.HTTPDownloadPort
                   ? "--download-port"
                   : null;

        #endregion

        #region (private static) TryParseAreas(Text, out Areas, out Problem)

        /// <summary>
        /// The areas as --areas gives them: names between commas, each one an
        /// area could have, none twice, and not the logs' every node shows over
        /// SFTP - as the "areas" of the section are held to.
        /// </summary>
        private static Boolean TryParseAreas(String?                    Text,
                                             out IReadOnlyList<String>  Areas,
                                             out String                 Problem)
        {

            Areas    = [];
            Problem  = "";

            var areas = new List<String>();

            foreach (var part in (Text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {

                if (!DownloadServerSettings.IsAreaName(part))
                {
                    Problem = $"'{part}' after --areas is no name of an area: lower-case letters, digits, '-' and '_', one part, up to 64.";
                    return false;
                }

                if (part == WWCPNode.SftpLogsArea)
                {
                    Problem = $"'{part}' after --areas is where every node shows its log files over SFTP: give the area another name.";
                    return false;
                }

                if (areas.Contains(part))
                {
                    Problem = $"'{part}' is named twice after --areas.";
                    return false;
                }

                areas.Add(part);

            }

            if (areas.Count == 0)
            {
                Problem = "Missing areas after --areas: one name, or several between commas, e.g. --areas firmware,configuration";
                return false;
            }

            if (areas.Count > DownloadServerSettings.MaxAreas)
            {
                Problem = $"--areas names {areas.Count} areas; {DownloadServerSettings.MaxAreas} at most.";
                return false;
            }

            Areas = areas;
            return true;

        }

        #endregion

        #region (private static) BesideTheWebInterface(Node) / OfTheKind(Node)

        /// <summary>
        /// Where files are offered, beside the web interface.
        /// </summary>
        private static IEnumerable<(String Label, String Value)> BesideTheWebInterface(DownloadServerNode Node)
        {
            yield return ("downloads", Node.ListenersText());
        }

        /// <summary>
        /// What the download server offers, how often it was fetched, and who
        /// may fetch it.
        /// </summary>
        private static IEnumerable<(String Label, String Value)> OfTheKind(DownloadServerNode Node)
        {

            var files = Node.Files(null);

            yield return ("offered",     $"{files.Count} file(s), {DownloadServerNode.SizeOf(files.Sum(file => file.Size))}, " +
                                         $"in {String.Join(", ", Node.Settings.Areas.Select(area => $"/{area}"))} of {Node.FilesDirectory}");

            yield return ("downloaded",  $"{Node.Downloads.Count} time(s), {DownloadServerNode.SizeOf(Node.Downloads.TotalBytes)}");

            var downloaders = Node.DownloaderAccounts.Count;

            yield return ("downloaders", downloaders == 0
                                             ? Node.Settings.AnonymousHTTP
                                                   ? $"none yet - anybody may download by HTTP; '{DownloadersCommand.CommandName} add <name>' makes one"
                                                   : $"none yet - '{DownloadersCommand.CommandName} add <name>' makes one"
                                             : $"{downloaders}");

        }

        #endregion


        public static async Task<Int32> Main(String[] Arguments)
        {

            Console.OutputEncoding = System.Text.Encoding.UTF8;

            #region Arguments

            // Every node's switches first. What is left is the download
            // server's own, as it was typed.
            var arguments = NodeArguments.Parse(Arguments);

            if (arguments.Refused(Usage) is Int32 refused)
                return refused;

            IPPort?                 downloadPort    = null;
            IPAddress?              downloadListen  = null;
            String?                 files           = null;
            IReadOnlyList<String>?  areas           = null;
            var                     noHTTP          = false;
            var                     noSFTP          = false;
            var                     anonymous       = false;

            var rest = arguments.Rest;

            for (var i = 0; i < rest.Count; i++)
            {
                switch (rest[i])
                {

                    case "--download-port":
                        if (i + 1 < rest.Count && UInt16.TryParse(rest[i + 1], out var parsedPort) && parsedPort > 0)
                        {
                            downloadPort = IPPort.Parse(parsedPort);
                            i++;
                        }
                        else
                        {
                            NodeProgram.Say(Console.Error, "Missing or invalid port number after --download-port!");
                            return 2;
                        }
                        break;

                    case "--download-listen":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out var listenText) ||
                            !DownloadServerSettings.TryParseListen(listenText, out downloadListen))
                        {
                            NodeProgram.Say(Console.Error, "Missing or invalid address after --download-listen: an IPv4 or IPv6 address, or \"any\"!");
                            return 2;
                        }
                        break;

                    case "--files":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out var directory) || String.IsNullOrWhiteSpace(directory))
                        {
                            NodeProgram.Say(Console.Error, "Missing directory after --files!");
                            return 2;
                        }
                        files = directory;
                        break;

                    case "--areas":
                        if (!NodeArguments.TryTakeValue(rest, ref i, out var areasText))
                        {
                            NodeProgram.Say(Console.Error, "Missing areas after --areas: one name, or several between commas, e.g. --areas firmware,configuration");
                            return 2;
                        }
                        if (!TryParseAreas(areasText, out var parsedAreas, out var areasProblem))
                        {
                            NodeProgram.Say(Console.Error, areasProblem);
                            return 2;
                        }
                        areas = parsedAreas;
                        break;

                    case "--no-http-downloads":
                        noHTTP = true;
                        break;

                    case "--no-sftp-downloads":
                        noSFTP = true;
                        break;

                    case "--anonymous-downloads":
                        anonymous = true;
                        break;

                    default:
                        return NodeArguments.Unknown(rest[i], Usage);

                }
            }

            var root = NodeProgram.RepositoryRoot("DownloadServerCLI.slnx");

            #endregion

            #region The download server

            DownloadServerNode node;

            try
            {
                node = new DownloadServerNode(
                           HTTPHostname:      arguments.HTTPHostname,
                           HTTPPort:          arguments.Port,
                           AccountsPath:      arguments.AccountsPathBelow(root),
                           ConfigFile:        new WWCPConfigFile(arguments.ConfigFilePathBelow(root)),
                           Frontend:          arguments.Frontend,
                           CertificatesPath:  arguments.CertificatesPath,
                           ConsoleLogLevel:   arguments.ConsoleLogLevel,
                           LogPath:           arguments.LogPathBelow(root),
                           BridgeDebugLog:    !arguments.NoTrace,
                           SSH:               arguments.SSH,
                           Web:               arguments.Web,

                           // The switches of this start, over the
                           // "downloadServer" section: for this start only,
                           // never written back.
                           CommandLine:       settings => settings with {
                                                  HTTPPort       = downloadPort   ?? settings.HTTPPort,
                                                  HTTPListen     = downloadListen ?? settings.HTTPListen,
                                                  Directory      = files          ?? settings.Directory,
                                                  Areas          = areas          ?? settings.Areas,
                                                  HTTP           = !noHTTP && settings.HTTP,
                                                  SFTP           = !noSFTP && settings.SFTP,
                                                  AnonymousHTTP  = anonymous || settings.AnonymousHTTP
                                              }
                       );
            }
            catch (Exception e)
            {
                return NodeProgram.CouldNotBeSetUp(DownloadServerNode.DownloadServerKind, e, arguments.Verbose);
            }

            await using (node)
            {

                if (node.ImportCertificates(arguments, out _) is Int32 notImported)
                    return notImported;

                if (arguments.ListCertificates)
                    node.ListCertificates();

                if (await node.Started(arguments.Verbose, WhatToDoAbout, SwitchOf: SwitchOf) is Int32 notStarted)
                    return notStarted;

                #region What somebody who just started this needs to know

                foreach (var line in node.Banner(BesideTheInterfaces:  BesideTheWebInterface(node),
                                                 OfTheKind:            OfTheKind(node)))
                    Console.WriteLine(line);

                #endregion

                #region The command line, until 'quit', Ctrl+C or SIGTERM

                // The node's: a prompt where somebody can type, and waiting
                // where nobody can, with the log sharing the screen.
                await new DownloadServerCLI(node).RunUntilStopped();

                #endregion

            }

            #endregion

            return 0;

        }

    }

}
