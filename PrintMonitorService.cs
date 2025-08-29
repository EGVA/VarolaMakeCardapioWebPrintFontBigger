using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

public class PrintMonitorService : BackgroundService
{
    private readonly ILogger<PrintMonitorService> _logger;
    private FileSystemWatcher _watcher;
    
    // Configuration values
    private string watchedPath = @"C:\Users\ericv\OneDrive\Documentos\Test\ToPrint";
    private string processedPath = @"C:\Users\ericv\OneDrive\Documentos\Test\Processed";
    private string printerName = "Cozinha";
    
    private static readonly object _fileLock = new object();

    public PrintMonitorService(ILogger<PrintMonitorService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Print Monitor Service starting...");

        // Check if the watched path exists.
        if (!Directory.Exists(watchedPath))
        {
            _logger.LogError("Watched directory not found at '{WatchedPath}'", watchedPath);
            return;
        }

        // Create the processed directory if it doesn't exist.
        if (!Directory.Exists(processedPath))
        {
            _logger.LogInformation("Creating processed directory at '{ProcessedPath}'", processedPath);
            Directory.CreateDirectory(processedPath);
        }

        InitializeFileWatcher();

        _logger.LogInformation("Service started. Watching for .RAW files in '{WatchedPath}'", watchedPath);

        // Keep the service running until cancelled
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }

        _watcher?.Dispose();
        _logger.LogInformation("Print Monitor Service stopped.");
    }

    private void InitializeFileWatcher()
    {
        _watcher = new FileSystemWatcher()
        {
            Path = watchedPath,
            Filter = "*.raw",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnNewFileCreated;
    }

    private void OnNewFileCreated(object sender, FileSystemEventArgs e)
    {
        // Use a lock to ensure only one file is processed at a time.
        lock (_fileLock)
        {
            string filePath = e.FullPath;
            string fileName = Path.GetFileName(filePath);
            string destinationPath = Path.Combine(processedPath, fileName);
            
            _logger.LogInformation("Processing new file: {FileName}", fileName);

            try
            {
                // Wait for the file to be fully written to disk
                Thread.Sleep(500);
                
                // Read and process the file.
                byte[] originalData = File.ReadAllBytes(filePath);

                // Process the file to apply double size formatting while preserving special characters
                byte[] modifiedData = ProcessFileWithDoubleSize(originalData);

                // Allocate unmanaged memory
                IntPtr pUnmanagedBytes = Marshal.AllocCoTaskMem(modifiedData.Length);
                Marshal.Copy(modifiedData, 0, pUnmanagedBytes, modifiedData.Length);

                // Send to printer
                if (RawPrinterHelper.SendBytesToPrinter(printerName, pUnmanagedBytes, modifiedData.Length))
                {
                    _logger.LogInformation("Successfully printed: {FileName}", fileName);
                    
                    // Move file
                    try
                    {
                        File.Move(filePath, destinationPath);
                        _logger.LogInformation("Moved file to processed folder: {FileName}", fileName);
                    }
                    catch (Exception moveEx)
                    {
                        _logger.LogWarning(moveEx, "Printed but could not move file: {FileName}", fileName);
                    }
                }

                Marshal.FreeCoTaskMem(pUnmanagedBytes);
            }
            catch (Win32Exception ex)
            {
                _logger.LogError(ex, "Print Error: Failed to print {FileName}", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error with file: {FileName}", fileName);
            }
        }
    }

    private byte[] ProcessFileWithDoubleSize(byte[] data)
    {
        // ... (same ProcessFileWithDoubleSize method as before)
        byte escByte = 0x1B;
        byte exclamationByte = 0x21;
        byte doubleSizeParameter = 0x30;
        byte dashByte = 0x2D;
        int separatorLength = 42;

        List<byte> result = new List<byte>();
        int i = 0;

        while (i < data.Length)
        {
            if (i <= data.Length - separatorLength)
            {
                bool isSeparator = true;
                for (int j = 0; j < separatorLength; j++)
                {
                    if (data[i + j] != dashByte)
                    {
                        isSeparator = false;
                        break;
                    }
                }

                if (isSeparator)
                {
                    for (int j = 0; j < separatorLength; j++)
                    {
                        result.Add(data[i + j]);
                    }
                    i += separatorLength;
                    continue;
                }
            }

            if (i <= data.Length - 3 && data[i] == escByte && data[i + 1] == exclamationByte)
            {
                result.Add(escByte);
                result.Add(exclamationByte);
                result.Add(doubleSizeParameter);
                i += 3;
            }
            else
            {
                result.Add(data[i]);
                i++;
            }
        }

        bool hasEscCommand = false;
        for (int j = 0; j < result.Count - 2; j++)
        {
            if (result[j] == escByte && result[j + 1] == exclamationByte)
            {
                hasEscCommand = true;
                break;
            }
        }

        if (!hasEscCommand)
        {
            result.InsertRange(0, new byte[] { escByte, exclamationByte, doubleSizeParameter });
        }

        return result.ToArray();
    }
}

// Keep the RawPrinterHelper class as is
public class RawPrinterHelper
{
    // ... (same RawPrinterHelper class as before)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)] public string pDocName;
        [MarshalAs(UnmanagedType.LPStr)] public string pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)] public string pDataType;
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartDocPriner(IntPtr hPrinter, Int32 level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

    [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, Int32 dwCount, out Int32 dwWritten);

    public static bool SendBytesToPrinter(string szPrinterName, IntPtr pBytes, Int32 dwCount)
    {
        IntPtr hPrinter = new IntPtr(0);
        DOCINFOA di = new DOCINFOA();
        Int32 dwWritten = 0;
        bool bSuccess = false;

        di.pDocName = "RAW Print Document";
        di.pDataType = "RAW";

        if (OpenPrinter(szPrinterName.Normalize(), out hPrinter, IntPtr.Zero))
        {
            if (StartDocPriner(hPrinter, 1, di))
            {
                if (StartPagePrinter(hPrinter))
                {
                    bSuccess = WritePrinter(hPrinter, pBytes, dwCount, out dwWritten);
                    EndPagePrinter(hPrinter);
                }
                EndDocPrinter(hPrinter);
            }
            ClosePrinter(hPrinter);
        }

        if (bSuccess == false)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        return bSuccess;
    }
}