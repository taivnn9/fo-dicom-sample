// Copyright (c) 2012-2022 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using System;
using System.IO;
using System.Threading.Tasks;
using FellowOakDicom;
using FellowOakDicom.Log;
using FellowOakDicom.Network;
using FellowOakDicom.Network.Client;
using NLog.Config;
using NLog.Targets;
using System.Linq;
using System.Net.Http;
using Topshelf;

namespace ConsoleTest
{
    internal static class Program
    {

        private static async Task Main(string[] args)
        {
            try
            {
                StartAutoDicomService();
                //StoreDicomInDirectory("");
                //Sample();
            }
            catch (Exception e)
            {
                if (!(e is DicomException))
                {
                    Console.WriteLine(e.ToString());
                }
            }

            Console.ReadLine();
        }
        static void StartAutoDicomService()
        {
            var exitCode = HostFactory.Run(x =>
            {
                x.Service<QuerySCUService>(s =>
                {
                    s.ConstructUsing(heartbeat => new QuerySCUService()); s.WhenStarted(heartbeat => heartbeat.Start()); s.WhenStopped(heartbeat => heartbeat.Stop());
                }); x.RunAsLocalSystem();
                x.SetServiceName("HeartbeatService"); x.SetDisplayName("Heartbeat Service"); x.SetDescription("This is the sample heartbeat service used in a YouTube demo.");
            });
        }
        /// <summary>
        /// The method will read all files in a directory/sub-directories and send a DICOMweb Store request (STOW-RS)
        /// Each 5 DICOM files will be grouped as a multi-part content and sent in a single request.
        /// </summary>
        /// <param name="directory"></param>
        static void StoreDicomInDirectory(string directory)
        {
            var mimeType = "application/dicom";
            MultipartContent multiContent = GetMultipartContent(mimeType);
            int count = 0;

            //Enumerate all files in a directory/sub-directories
            foreach (var path in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                count++;

                StreamContent sContent = new StreamContent(File.OpenRead(path));

                sContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);

                multiContent.Add(sContent);

                if (count % 5 == 0)
                {
                    count = 0;

                    StoreToServer(multiContent);

                    multiContent = GetMultipartContent(mimeType);
                }
            }

            //Flush any remaining images (should be less than 5)
            if (multiContent.Count() > 0)
            {
                StoreToServer(multiContent);
            }
        }

        /// <summary>
        /// Get a valid multipart content.
        /// </summary>
        /// <param name="mimeType"></param>
        /// <returns></returns>
        private static MultipartContent GetMultipartContent(string mimeType)
        {
            var multiContent = new MultipartContent("related", "DICOM DATA BOUNDARY");

            multiContent.Headers.ContentType.Parameters.Add(new System.Net.Http.Headers.NameValueHeaderValue("type", "\"" + mimeType + "\""));
            return multiContent;
        }

        /// <summary>
        /// Send the multipart content to the server using the STOW-RS service
        /// </summary>
        /// <param name="multiContent"></param>
        private static void StoreToServer(MultipartContent multiContent)
        {
            try
            {
                string url = "http://localhost:44301/stowrs/";
                HttpClient client = new HttpClient();

                var request = new HttpRequestMessage(HttpMethod.Post, url);

                request.Content = multiContent;

                var result = client.SendAsync(request);

                result.Wait();

                HttpResponseMessage response = result.Result;

                Console.WriteLine(response.StatusCode);

                var result2 = response.Content.ReadAsStringAsync();

                result2.Wait();

                string responseText = result2.Result;

                Console.WriteLine(responseText);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(ex.ToString());
            }
        }

        static async Task Sample()
        {

            // Initialize log manager.
            new DicomSetupBuilder().RegisterServices(
               s => s.AddFellowOakDicom().AddLogManager<NLogManager>()
               ).Build();

            DicomException.OnException += delegate (object sender, DicomExceptionEventArgs ea)
            {
                ConsoleColor old = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(ea.Exception);
                Console.ForegroundColor = old;
            };

            var config = new LoggingConfiguration();

            var target = new ColoredConsoleTarget
            {
                Layout = @"${date:format=HH\:mm\:ss}  ${message}"
            };
            config.AddTarget("Console", target);
            config.LoggingRules.Add(new LoggingRule("*", NLog.LogLevel.Debug, target));

            NLog.LogManager.Configuration = config;

            var client = DicomClientFactory.Create("127.0.0.1", 11112, false, "SCU", "STORESCP");
            client.NegotiateAsyncOps();
            for (int i = 0; i < 10; i++)
            {
                await client.AddRequestAsync(new DicomCEchoRequest());
            }

            await client.AddRequestAsync(new DicomCStoreRequest(@"test1.dcm"));
            await client.AddRequestAsync(new DicomCStoreRequest(@"test2.dcm"));
            await client.SendAsync();

            foreach (DicomPresentationContext ctr in client.AdditionalPresentationContexts)
            {
                Console.WriteLine("PresentationContext: " + ctr.AbstractSyntax + " Result: " + ctr.Result);
            }

            var samplesDir = Path.Combine(
                Path.GetPathRoot(Environment.CurrentDirectory),
                "Development",
                "fo-dicom-samples");
            var testDir = Path.Combine(samplesDir, "Test");

            if (!Directory.Exists(testDir))
            {
                Directory.CreateDirectory(testDir);
            }
        }
    }
}
