// Copyright (c) 2012-2018 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using Dicom;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace SimpleViewer.Universal.Services
{

   public class DicomFileReaderService : IDicomFileReaderService
    {
        public async Task<IList<DicomFile>> GetFilesAsync()
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".dcm");
            picker.FileTypeFilter.Add(".dic");

            var picks = await picker.PickMultipleFilesAsync().AsTask().ConfigureAwait(false);
            if (picks == null)
            {
                return null;
            }

            var streams = await Task.WhenAll(picks.Select(pick => pick.OpenStreamForReadAsync())).ConfigureAwait(false);
            var files = await Task.WhenAll(streams.Select(DicomFile.OpenAsync)).ConfigureAwait(false);

            return files.Where(file => file != null).ToList();
        }
    }
}