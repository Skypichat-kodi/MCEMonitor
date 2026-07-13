using System;
using System.Runtime.InteropServices;

namespace MediaMonitor.Core.Interop
{
    internal static class WMF
    {
        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFStartup(int version, int flags);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        public static extern int MFShutdown();

        [DllImport("mfreadwrite.dll", ExactSpelling = true)]
        public static extern int MFCreateSourceReaderFromURL(
            [MarshalAs(UnmanagedType.LPWStr)] string url,
            IntPtr attributes,
            out IMFSourceReader reader);
    }

    [ComImport, Guid("70ae66f2-c8b1-4e3f-9f0f-5d0c3f5e3c6f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFSourceReader
    {
        int GetNativeMediaType(int streamIndex, int mediaTypeIndex, out IMFMediaType mediaType);
        // autres méthodes non nécessaires pour la durée
    }

    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMFMediaType
    {
        int GetUINT64([MarshalAs(UnmanagedType.LPStruct)] Guid key, out long value);
    }

    internal static class MFAttributes
    {
        public static readonly Guid MF_PD_DURATION = new Guid("6c990d31-bb8e-477a-8598-0d5d96fcd88a");
    }

    internal static class MF_SOURCE_READER
    {
        public const int FirstAudioStream = unchecked((int)0xFFFFFFFF);
        public const int FirstVideoStream = unchecked((int)0xFFFFFFFE);
    }
}

