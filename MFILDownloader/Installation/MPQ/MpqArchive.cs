using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using MFILDownloader.Installation.MPQ.Native;

namespace MFILDownloader.Installation.MPQ
{
    public class MpqArchive : IDisposable
    {
        public readonly string FilePath;

        private MpqArchiveSafeHandle _handle;
        private ConcurrentDictionary<MpqFileStream, object> _openFiles;
        private readonly FileAccess _accessType;
        private HashSet<MpqArchiveCompactingEventHandler> _compactCallbacks;
        private SFILE_COMPACT_CALLBACK _compactCallback;

        #region Constructors / Factories
        private MpqArchive()
        {
            _openFiles = new ConcurrentDictionary<MpqFileStream, object>();
            _compactCallbacks = new HashSet<MpqArchiveCompactingEventHandler>();
        }


        public MpqArchive(string filePath, FileAccess accessType, OpenArchiveFlags dwflags = OpenArchiveFlags.None) : this()
        {
            FilePath = filePath;

            _accessType = accessType;
            OpenArchiveFlags flags = OpenArchiveFlags.TypeIsFile;
            if (accessType == FileAccess.Read)
                flags |= OpenArchiveFlags.AccessReadOnly;
            else
                flags |= OpenArchiveFlags.AccessReadWriteShare;

            flags |= dwflags;

            // constant 2 = SFILE_OPEN_HARD_DISK_FILE
            if (!NativeMethods.SFileOpenArchive(filePath, 2, flags, out _handle))
                throw new Win32Exception(); // Implicitly calls GetLastError
        }

        private MpqArchive(string filePath, MpqArchiveVersion version, MpqFileStreamAttributes listfileAttributes, MpqFileStreamAttributes attributesFileAttributes, int maxFileCount) : this()
        {
            if (maxFileCount < 0)
                throw new ArgumentException("maxFileCount");

            OpenArchiveFlags flags = OpenArchiveFlags.TypeIsFile | OpenArchiveFlags.AccessReadWriteShare;
            flags |= (OpenArchiveFlags)version;

            if (!NativeMethods.SFileCreateArchive(filePath, (uint)flags, int.MaxValue, out _handle))
                throw new Win32Exception();
        }

        public static MpqArchive CreateNew(string mpqPath, MpqArchiveVersion version)
        {
            return CreateNew(mpqPath, version, MpqFileStreamAttributes.None, MpqFileStreamAttributes.None, int.MaxValue);
        }

        public static MpqArchive CreateNew(string mpqPath, MpqArchiveVersion version, MpqFileStreamAttributes listfileAttributes, MpqFileStreamAttributes attributesFileAttributes, int maxFileCount)
        {
            return new MpqArchive(mpqPath, version, listfileAttributes, attributesFileAttributes, maxFileCount);
        }
        #endregion

        #region Properties
        public long MaxFileCount
        {
            get
            {
                VerifyHandle();
                return NativeMethods.SFileGetMaxFileCount(_handle);
            }
            set
            {
                if (value < 0 || value > uint.MaxValue)
                    throw new ArgumentException("value");
                VerifyHandle();

                if (!NativeMethods.SFileSetMaxFileCount(_handle, unchecked((uint)value)))
                    throw new Win32Exception();
            }
        }

        private void VerifyHandle()
        {
            if (_handle == null || _handle.IsInvalid)
                throw new ObjectDisposedException("MpqArchive");
        }

        public bool IsPatchedArchive
        {
            get
            {
                VerifyHandle();
                return NativeMethods.SFileIsPatchedArchive(_handle);
            }
        }
        #endregion

        public void Flush()
        {
            VerifyHandle();
            if (!NativeMethods.SFileFlushArchive(_handle))
                throw new Win32Exception();
        }

        public int AddListFile(string listfileContents)
        {
            VerifyHandle();
            return NativeMethods.SFileAddListFile(_handle, listfileContents);
        }

        public void AddFileFromDisk(string filePath, string archiveName, uint dwFlags = 0)
        {
            VerifyHandle();

            if (!NativeMethods.SFileAddFile(_handle, filePath, archiveName, dwFlags))
                throw new Win32Exception();
        }

        public void AddFileFromStream(MpqFileStream stream)
        {
            VerifyHandle();

#pragma warning disable CS1690
            uint dwCompression = (stream.TFileEntry.dwFlags & 0x200) == 0x200 ? 2u : 0; // zlib
#pragma warning restore CS1690

            if (NativeMethods.SFileCreateFile(_handle, stream.FileName, stream.TFileEntry, out MpqFileSafeHandle ptr))
            {
                using (var file = new MpqFileStream(ptr, FileAccess.Write, this))
                {
                    // LOH magic number
                    byte[] buffer = new byte[81920];
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
                        file.Write(buffer, 0, read, dwCompression);
                }
            }
        }


        public void Compact(string listfile)
        {
            VerifyHandle();
            if (!NativeMethods.SFileCompactArchive(_handle, listfile, false))
                throw new Win32Exception();
        }

        private void _OnCompact(IntPtr pvUserData, uint dwWorkType, ulong bytesProcessed, ulong totalBytes)
        {
            MpqArchiveCompactingEventArgs args = new MpqArchiveCompactingEventArgs(dwWorkType, bytesProcessed, totalBytes);
            OnCompacting(args);
        }

        protected virtual void OnCompacting(MpqArchiveCompactingEventArgs e)
        {
            foreach (var cb in _compactCallbacks)
                cb(this, e);
        }

        public event MpqArchiveCompactingEventHandler Compacting
        {
            add
            {
                VerifyHandle();
                _compactCallback = _OnCompact;
                if (!NativeMethods.SFileSetCompactCallback(_handle, _compactCallback, IntPtr.Zero))
                    throw new Win32Exception();

                _compactCallbacks.Add(value);
            }
            remove
            {
                _compactCallbacks.Remove(value);

                VerifyHandle();
                if (_compactCallbacks.Count == 0)
                {
                    if (!NativeMethods.SFileSetCompactCallback(_handle, null, IntPtr.Zero))
                    {
                        // Don't do anything here.  Remove shouldn't fail hard.
                    }
                }
            }
        }


        public void AddPatchArchive(string patchPath)
        {
            VerifyHandle();

            if (!NativeMethods.SFileOpenPatchArchive(_handle, patchPath, null, 0))
                throw new Win32Exception();
        }

        public void AddPatchArchives(IEnumerable<string> patchPaths)
        {
            if (patchPaths == null || !patchPaths.Any())
                return;

            VerifyHandle();

            foreach (string path in patchPaths)
            {
                // Don't sublet to AddPatchArchive to avoid having to repeatedly call VerifyHandle()
                if (!NativeMethods.SFileOpenPatchArchive(_handle, path, null, 0))
                    throw new Win32Exception();
            }
        }

        public bool HasFile(string fileToFind)
        {
            VerifyHandle();

            return NativeMethods.SFileHasFile(_handle, fileToFind) && (NativeMethods.SFileVerifyFile(_handle, fileToFind, 0) & 1) == 0;
        }

        public MpqFileStream OpenFile(string fileName)
        {
            VerifyHandle();

            if (!NativeMethods.SFileOpenFileEx(_handle, fileName, 0, out MpqFileSafeHandle fileHandle))
                throw new Win32Exception();

            MpqFileStream fs = new MpqFileStream(fileHandle, _accessType, this);
            _openFiles.TryAdd(fs, null);
            return fs;
        }

        public void ExtractFile(string fileToExtract, string destinationPath)
        {
            VerifyHandle();

            if (!NativeMethods.SFileExtractFile(_handle, fileToExtract, destinationPath, 0))
                throw new Win32Exception();
        }

        public MpqFileVerificationResults VerifyFile(string fileToVerify)
        {
            VerifyHandle();

            return (MpqFileVerificationResults)NativeMethods.SFileVerifyFile(_handle, fileToVerify, 0);
        }

        public MpqArchiveVerificationResult VerifyArchive()
        {
            VerifyHandle();

            return (MpqArchiveVerificationResult)NativeMethods.SFileVerifyArchive(_handle);
        }


        #region IDisposable implementation
        public void Dispose()
        {
            Dispose(true);
        }

        ~MpqArchive()
        {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Release owned files first.
                if (_openFiles != null)
                {
                    foreach (MpqFileStream of in _openFiles.Keys)
                        of.Dispose();

                    _openFiles.Clear();
                    _openFiles = null;
                }

                // Release
                if (_handle != null && !_handle.IsInvalid)
                {
                    _handle.Close();
                    _handle = null;
                }
            }
        }

        internal void RemoveOwnedFile(MpqFileStream file)
        {
            _openFiles.TryRemove(file, out _);
        }

        #endregion
    }

    public enum MpqArchiveVersion
    {
        Version1 = 0,
        Version2 = 0x01000000,
        Version3 = 0x02000000,
        Version4 = 0x03000000,
    }

    [Flags]
    public enum MpqFileStreamAttributes
    {
        None = 0x0,
    }

    [Flags]
    public enum MpqFileVerificationResults
    {
        /// <summary>
        /// There were no errors with the file.
        /// </summary>
        Verified = 0,
        /// <summary>
        /// Failed to open the file
        /// </summary>
        Error = 0x1,
        /// <summary>
        /// Failed to read all data from the file
        /// </summary>
        ReadError = 0x2,
        /// <summary>
        /// File has sector CRC
        /// </summary>
        HasSectorCrc = 0x4,
        /// <summary>
        /// Sector CRC check failed
        /// </summary>
        SectorCrcError = 0x8,
        /// <summary>
        /// File has CRC32
        /// </summary>
        HasChecksum = 0x10,
        /// <summary>
        /// CRC32 check failed
        /// </summary>
        ChecksumError = 0x20,
        /// <summary>
        /// File has data MD5
        /// </summary>
        HasMd5 = 0x40,
        /// <summary>
        /// MD5 check failed
        /// </summary>
        Md5Error = 0x80,
        /// <summary>
        /// File has raw data MD5
        /// </summary>
        HasRawMd5 = 0x100,
        /// <summary>
        /// Raw MD5 check failed
        /// </summary>
        RawMd5Error = 0x200,
    }

    public enum MpqArchiveVerificationResult
    {
        /// <summary>
        /// There is no signature in the MPQ
        /// </summary>
        NoSignature = 0,
        /// <summary>
        /// There was an error during verifying signature (like no memory)
        /// </summary>
        VerificationFailed = 1,
        /// <summary>
        /// There is a weak signature and sign check passed
        /// </summary>
        WeakSignatureVerified = 2,
        /// <summary>
        /// There is a weak signature but sign check failed
        /// </summary>
        WeakSignatureFailed = 3,
        /// <summary>
        /// There is a strong signature and sign check passed
        /// </summary>
        StrongSignatureVerified = 4,
        /// <summary>
        /// There is a strong signature but sign check failed
        /// </summary>
        StrongSignatureFailed = 5,
    }
}
