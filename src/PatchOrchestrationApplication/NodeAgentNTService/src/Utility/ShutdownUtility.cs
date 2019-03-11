// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.ServiceFabric.PatchOrchestration.NodeAgentNTService.Utility
{
    using System;
    using System.ComponentModel;
    using System.Globalization;
    using System.Runtime.InteropServices;

    class ShutdownUtility
    {
        internal const int SHTDN_REASON_MAJOR_OTHER = 0x00000000;
        internal const int SHTDN_REASON_MINOR_OTHER = 0x00000000;
        internal const int SE_PRIVILEGE_ENABLED = 0x00000002;
        internal const int ERROR_NOT_ALL_ASSIGNED = 1300;
        internal const UInt32 TOKEN_QUERY = 0x0008;
        internal const UInt32 TOKEN_ADJUST_PRIVILEGES = 0x0020;

        public static void EnablePrivilege()
        {                      
            try
            {
                var locallyUniqueIdentifier = new LUID();

                if (LookupPrivilegeValue(null, "SeShutdownPrivilege", ref locallyUniqueIdentifier))
                {
                    var TOKEN_PRIVILEGES = new TOKEN_PRIVILEGES();
                    TOKEN_PRIVILEGES.PrivilegeCount = 1;
                    TOKEN_PRIVILEGES.Attributes = SE_PRIVILEGE_ENABLED;
                    TOKEN_PRIVILEGES.Luid = locallyUniqueIdentifier;

                    var tokenHandle = IntPtr.Zero;
                    try
                    {
                        var currentProcess = GetCurrentProcess();
                        if (OpenProcessToken(currentProcess,
                            TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out tokenHandle))
                        {
                            if (AdjustTokenPrivileges(tokenHandle, false,
                                ref TOKEN_PRIVILEGES,
                                1024, IntPtr.Zero, IntPtr.Zero))
                            {
                                var lastError = Marshal.GetLastWin32Error();
                                if (lastError == ERROR_NOT_ALL_ASSIGNED)
                                {
                                    var win32Exception = new Win32Exception();
                                    throw new InvalidOperationException("AdjustTokenPrivileges failed.", win32Exception);
                                }
                            }
                            else
                            {
                                var win32Exception = new Win32Exception();
                                throw new InvalidOperationException("AdjustTokenPrivileges failed.", win32Exception);
                            }
                        }
                        else
                        {
                            var win32Exception = new Win32Exception();

                            var exceptionMessage = string.Format(CultureInfo.InvariantCulture,
                                "OpenProcessToken failed. CurrentProcess: {0}",
                                currentProcess.ToInt32());

                            throw new InvalidOperationException(exceptionMessage, win32Exception);
                        }
                    }
                    finally
                    {
                        if (tokenHandle != IntPtr.Zero)
                            CloseHandle(tokenHandle);
                    }
                }
                else
                {
                    var win32Exception = new Win32Exception();

                    var exceptionMessage = string.Format(CultureInfo.InvariantCulture,
                        "LookupPrivilegeValue failed. SecurityEntityValue: SeShutdownPrivilege");

                    throw new InvalidOperationException(exceptionMessage, win32Exception);
                }
            }
            catch (Exception e)
            {
                var exceptionMessage = string.Format(CultureInfo.InvariantCulture,
                    "GrandPrivilege failed. SE_SHUTDOWN_NAME ");

                throw new InvalidOperationException(exceptionMessage, e);
            }
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool InitiateSystemShutdownEx(
        string lpMachineName,
        string lpMessage,
        uint dwTimeout,
        bool bForceAppsClosed,
        bool bRebootAfterShutdown,
        UInt32 dwReason);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool LookupPrivilegeValue(string lpsystemname, string lpname, [MarshalAs(UnmanagedType.Struct)] ref LUID lpLuid);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AdjustTokenPrivileges(IntPtr tokenhandle,
                                 [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
                                 [MarshalAs(UnmanagedType.Struct)]ref TOKEN_PRIVILEGES newstate,
                                 uint bufferlength, IntPtr previousState, IntPtr returnlength);
               
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport("Advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(IntPtr processHandle,
                            uint desiredAccesss,
                            out IntPtr tokenHandle);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern Boolean CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        internal struct LUID
        {
            internal Int32 LowPart;
            internal UInt32 HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TOKEN_PRIVILEGES
        {
            internal Int32 PrivilegeCount;
            internal LUID Luid;
            internal Int32 Attributes;
        }
    }
}
