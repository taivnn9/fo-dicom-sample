using FellowOakDicom.Network.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FellowOakDicom;
using FellowOakDicom.Network;
using System.Configuration;
using FluentScheduler;

namespace ConsoleTest
{
    public class QuerySCUService
    {
        private int _intervalMinute = 1;
        private string _storagePath = @".\DICOM";
        private string _Aet = "FO-DICOM";

        private string _qrServerHost = "www.dicomserver.co.uk";
        private int _qrServerPort = 104;
        private string _qrServerAET = "STORESCP";

        private string _dtServerHost = "127.0.0.1";
        private int _dtServerPort = 11112;
        private string _dtServerAET = "SERVERAE1";


        private static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public QuerySCUService()
        {
            logger.Info($"---------------------------------------------------------------------");
            logger.Info($"Start QuerySCUService");

            _intervalMinute = int.Parse(ConfigurationManager.AppSettings["_intervalMinute"]);
            _storagePath = ConfigurationManager.AppSettings["_storagePath"];
            _Aet = ConfigurationManager.AppSettings["_Aet"];

            _qrServerHost = ConfigurationManager.AppSettings["_qrServerHost"];
            _qrServerPort = int.Parse(ConfigurationManager.AppSettings["_qrServerPort"]);
            _qrServerAET = ConfigurationManager.AppSettings["_qrServerAET"];

            _dtServerHost = ConfigurationManager.AppSettings["_dtServerHost"];
            _dtServerPort = int.Parse(ConfigurationManager.AppSettings["_dtServerPort"]);
            _dtServerAET = ConfigurationManager.AppSettings["_dtServerAET"];

            logger.Info($"Store path: {_storagePath}");
            logger.Info($"Store AET: {_Aet}");
            logger.Info($"Delay: {_intervalMinute} minute");
            logger.Info($"Query Server Host: {_qrServerHost}");
            logger.Info($"Query Server Port: {_qrServerPort}");
            logger.Info($"Query Server AET: {_qrServerAET}");
            logger.Info($"Store Server Host: {_dtServerHost}");
            logger.Info($"Store Server Port: {_dtServerPort}");
            logger.Info($"Store Server AET: {_dtServerAET}");

        }

        public void Start()
        {
            JobManager.Initialize();

            JobManager.AddJob(
                () => timer_Elapsed(),
                s => s.ToRunEvery(_intervalMinute).Minutes()
            );
        }
        public void Stop()
        {
            JobManager.Stop();
        }

        private async void timer_Elapsed()
        {

            //Dicom.Log.LogManager.SetImplementation(ConsoleLogManager.Instance
            //// Query example: http://dicomiseasy.blogspot.com/2012/01/dicom-queryretrieve-part-i.html
            // Query study >= current day);
            logger.Info($"-------------------------------");
            logger.Debug($"C-Find Study");
            await CFindStudiesByDate("", $"{DateTime.Now.AddDays(-1).ToString("yyyyMMdd")}-", "");

            //Console.ReadLine();
        }

        private async Task CFindStudiesByDate(string patientName, string studyDate, string studyTime)
        {
            var client = DicomClientFactory.Create(_qrServerHost, _qrServerPort, false, _Aet, _qrServerAET);
            client.NegotiateAsyncOps();

            var request = new DicomCFindRequest(DicomQueryRetrieveLevel.Study);
            request.Dataset.AddOrUpdate(new DicomTag(0x8, 0x5), "ISO_IR 100"); // always add the encoding


            request.Dataset.AddOrUpdate(DicomTag.PatientName, patientName);
            request.Dataset.AddOrUpdate(DicomTag.PatientID, String.Empty);
            request.Dataset.AddOrUpdate(DicomTag.PatientBirthDate, String.Empty);

            request.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, String.Empty);
            request.Dataset.AddOrUpdate(DicomTag.StudyDate, studyDate);
            request.Dataset.AddOrUpdate(DicomTag.StudyTime, studyTime);
            request.Dataset.AddOrUpdate(DicomTag.StudyDescription, String.Empty);
            request.Dataset.AddOrUpdate(DicomTag.InstanceNumber, String.Empty);
            request.Dataset.AddOrUpdate(DicomTag.InstanceAvailability, String.Empty);
            request.Dataset.AddOrUpdate(DicomTag.ModalitiesInStudy, String.Empty);

            var studyUids = new HashSet<string>();
            var responses = new HashSet<DicomCFindResponse>();
            request.OnResponseReceived += (req, response) =>
            {
                //DebugStudyResponse(response);
                responses.Add(response);
                studyUids.Add(response.Dataset?.GetSingleValue<string>(DicomTag.StudyInstanceUID));
            };
            await client.AddRequestAsync(request);
            try
            {
                client.SendAsync().Wait();
            }
            catch (Exception ex)
            {
                logger.Error($"C-Find Study exception {ex.Message} {ex.StackTrace}");
            }


            logger.Debug($"C-Find Study completed. Found {studyUids.Count} studies");

            foreach (string studyUid in studyUids)
            {
                if (studyUid != null)
                {
                    logger.Debug($"------------");
                    logger.Debug($"C-Find Series");

                    var studyUidPath = Path.GetFullPath(_storagePath);
                    studyUidPath = Path.Combine(studyUidPath, studyUid);

                    if (!Directory.Exists(studyUidPath))
                    {
                        Directory.CreateDirectory(studyUidPath);

                        DicomCFindResponse response = responses.Where(x => x.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty).Equals(studyUid)).FirstOrDefault();
                        DebugStudyResponse(response, true);
                        CFindSeriesByStudy(studyUid);
                    }
                    else
                    {
                        logger.Debug($"C-Find Ignore because Study already exited {studyUid}");
                    }
                }

            }
        }
        private async void CFindSeriesByStudy(string studyUid)
        {
            IDicomClient client = DicomClientFactory.Create(_qrServerHost, _qrServerPort, false, _Aet, _qrServerAET);
            var request = new DicomCFindRequest(DicomQueryRetrieveLevel.Series);
            request.Dataset.AddOrUpdate(new DicomTag(0x8, 0x5), "ISO_IR 100"); // always add the encoding

            // add the dicom tags with empty values that should be included in the result
            request.Dataset.AddOrUpdate(DicomTag.SeriesInstanceUID, "");
            request.Dataset.AddOrUpdate(DicomTag.SeriesDescription, "");
            request.Dataset.AddOrUpdate(DicomTag.Modality, "");
            request.Dataset.AddOrUpdate(DicomTag.NumberOfSeriesRelatedInstances, "");

            // add the dicom tags that contain the filter criterias
            request.Dataset.AddOrUpdate(DicomTag.StudyInstanceUID, studyUid);

            var serieUids = new HashSet<string>();
            var responses = new HashSet<DicomCFindResponse>();
            request.OnResponseReceived += (req, response) =>
            {
                //DebugSerieResponse(response, false);
                responses.Add(response);
                serieUids.Add(response.Dataset?.GetSingleValue<string>(DicomTag.SeriesInstanceUID));
            };
            await client.AddRequestAsync(request);
            try
            {
                client.SendAsync().Wait();
            }
            catch (Exception ex)
            {
                logger.Error($"C-Find Series exception {ex.Message} {ex.StackTrace}");
            }

            logger.Debug($"C-Find Series completed. Found {serieUids.Count} Series");
            foreach (string seriesUid in serieUids)
            {
                if (seriesUid != null)
                {
                    DicomCFindResponse response = responses.Where(x => x.Dataset.GetSingleValueOrDefault(DicomTag.SeriesInstanceUID, string.Empty).Equals(seriesUid)).FirstOrDefault();
                    //DebugSerieResponse(response, false);

                    logger.Debug($"C-Get StudyUID {studyUid} SeriesUID {seriesUid}");
                    CGetDicomBySeries(studyUid, seriesUid);
                }
            };
        }
        private async void CGetDicomBySeries(string studyUid, string seriesUid)
        {
            IDicomClient client = DicomClientFactory.Create(_qrServerHost, _qrServerPort, false, _Aet, _qrServerAET);
            DicomCGetRequest request = new DicomCGetRequest(studyUid, seriesUid);
            client.OnCStoreRequest += (DicomCStoreRequest req) =>
            {
                logger.Debug($"C-Get StudyUID {studyUid} SeriesUID {seriesUid} done");
                SaveImage(req.Dataset);
                return Task.FromResult(new DicomCStoreResponse(req, DicomStatus.Success));
            };
            // the client has to accept storage of the images. We know that the requested images are of SOP class Secondary capture, 
            // so we add the Secondary capture to the additional presentation context
            // a more general approach would be to mace a cfind-request on image level and to read a list of distinct SOP classes of all
            // the images. these SOP classes shall be added here.
            var pcs = DicomPresentationContext.GetScpRolePresentationContextsFromStorageUids(
                DicomStorageCategory.Image,
                DicomTransferSyntax.JPEG2000Lossless
                //DicomTransferSyntax.ExplicitVRLittleEndian,
                //DicomTransferSyntax.ImplicitVRLittleEndian,
                //DicomTransferSyntax.ImplicitVRBigEndian
                );
            client.AdditionalPresentationContexts.AddRange(pcs);
            await client.AddRequestAsync(request);
            try
            {
                client.SendAsync().Wait();
            }
            catch (Exception ex)
            {
                logger.Debug($"C-Get StudyUID {studyUid} SeriesUID {seriesUid} exception {ex.Message} {ex.StackTrace}");
                //logger.Error(ex);
            }


        }

        // Example create CFind, CStore request https://searchcode.com/codesearch/view/75288652/
        private static void DebugStudyResponse(DicomCFindResponse response, bool ignoreDebug)
        {
            try
            {
                if (response.Status == DicomStatus.Pending || ignoreDebug)
                {

                    logger.Debug($"Study Instance UID: {response.Dataset.GetSingleValueOrDefault(DicomTag.StudyInstanceUID, string.Empty)} ");
                    logger.Debug($"Study Date: {response.Dataset.GetSingleValueOrDefault(DicomTag.StudyDate, string.Empty)} ");
                    logger.Debug($"Study Time: {response.Dataset.GetSingleValueOrDefault(DicomTag.StudyTime, string.Empty)} ");
                    logger.Debug($"Instances: {response.Dataset.GetSingleValueOrDefault(DicomTag.InstanceNumber, string.Empty)} ");
                    logger.Debug($"Availability: {response.Dataset.GetSingleValueOrDefault(DicomTag.InstanceAvailability, string.Empty)} ");
                    logger.Debug($"Modalities: {(response.Dataset.TryGetString(DicomTag.ModalitiesInStudy, out var dummy) ? dummy : string.Empty)} ");
                    logger.Debug($"Patient ID: {response.Dataset.GetSingleValueOrDefault(DicomTag.PatientID, string.Empty)} ");
                    logger.Debug($"Patient name: {response.Dataset.GetSingleValueOrDefault(DicomTag.PatientName, string.Empty)} ");
                    logger.Debug($"Patient DOB: {response.Dataset.GetSingleValueOrDefault(DicomTag.PatientBirthDate, string.Empty)} ");
                }
            }
            catch (Exception ex)
            {
                // ignore exception
            }
        }
        private static void DebugSerieResponse(DicomCFindResponse response, bool ignoreDebug)
        {
            try
            {
                if (response.Status == DicomStatus.Pending || ignoreDebug)
                {
                    logger.Debug($"SerieUID: {response.Dataset.GetSingleValue<string>(DicomTag.SeriesInstanceUID)}");
                    logger.Debug($"Serie description: {response.Dataset.GetSingleValue<string>(DicomTag.SeriesDescription)}");
                    logger.Debug($"Modality: {response.Dataset.GetSingleValue<string>(DicomTag.Modality)} ");
                    logger.Debug($"Instances: {response.Dataset.GetSingleValue<int>(DicomTag.NumberOfSeriesRelatedInstances)} ");
                }
            }
            catch (Exception)
            {
                // ignore errors
            }
        }
        private void SaveImage(DicomDataset dataset)
        {
            var studyUid = dataset.GetSingleValue<string>(DicomTag.StudyInstanceUID).Trim();
            var instUid = dataset.GetSingleValue<string>(DicomTag.SOPInstanceUID).Trim();

            var path = Path.GetFullPath(_storagePath);
            path = Path.Combine(path, studyUid);

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            var filePath = Path.Combine(path, instUid) + ".dcm";
            if (!File.Exists(filePath))
            {
                new DicomFile(dataset).Save(filePath);
                //logger.Debug($"Dicom file saved at: {filePath}");

            }
            else
            {
                //logger.Debug($"Ignore save dicom file because file already exist at: {filePath}");
            }

            //logger.Debug($"Sent data to PACs Linhsoft");
        }

    }
}
