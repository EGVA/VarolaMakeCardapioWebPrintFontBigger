using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Threading;

public class RawPrinterHelper
{
    // Structure and API declarions for raw printing.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string pDocName;
        [MarshalAs(UnmanagedType.LPStr)]
        public string pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)]
        public string pDataType;
    }

    [DllImport("winspool.Drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.Drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartDocPrinter(IntPtr hPrinter, Int32 level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

    [DllImport("winspool.Drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.Drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, Int32 dwCount, out Int32 dwWritten);

    /// <summary>
    /// Sends a byte array to the specified printer.
    /// </summary>
    /// <param name="szPrinterName">The name of the printer.</param>
    /// <param name="pBytes">The byte array to send.</param>
    /// <param name="dwCount">The number of bytes to send.</param>
    /// <returns>true on success, false on failure.</returns>
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
            if (StartDocPrinter(hPrinter, 1, di))
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

public class Program
{
    // A simple lock to prevent multiple files from being processed at the exact same time.
    private static readonly object _fileLock = new object();
    
    public static void Main(string[] args)
    {
        // --- IMPORTANT: CHANGE THESE VALUES ---
        // The folder to watch for new .RAW files.
        string watchedPath = @"C:\Users\ericv\OneDrive\Documentos\Test\ToPrint";
        // The folder to move the printed files to.
        string processedPath = @"C:\Users\ericv\OneDrive\Documentos\Test\Processed";
        // The name of your printer.
        string printerName = "Caixa ID";
        // -----------------------------------------

        // Check if the watched path exists.
        if (!Directory.Exists(watchedPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Watched directory not found at '{watchedPath}'");
            Console.ResetColor();
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
            return;
        }

        // Create the processed directory if it doesn't exist.
        if (!Directory.Exists(processedPath))
        {
            Console.WriteLine($"Creating processed directory at '{processedPath}'...");
            Directory.CreateDirectory(processedPath);
        }

        // Set up the FileSystemWatcher.
        Console.WriteLine($"Watching for new .RAW files in '{watchedPath}'...");
        
        using (FileSystemWatcher watcher = new FileSystemWatcher())
        {
            watcher.Path = watchedPath;

            // Only watch for files that end with .raw
            watcher.Filter = "*.raw";

            // Only raise events for changes to the file's name and its last write time.
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;

            // Add event handlers.
            // This event is raised when a file is created.
            watcher.Created += (sender, e) => OnNewFileCreated(e, printerName, processedPath);
            
            // Begin watching.
            watcher.EnableRaisingEvents = true;

            // The application will now run indefinitely, waiting for file creation events.
            Console.WriteLine("Press 'q' to quit the application.");
            while (Console.Read() != 'q') ;
        }
    }
    
    /// <summary>
    /// Event handler for when a new file is created in the watched directory.
    /// </summary>
    private static void OnNewFileCreated(FileSystemEventArgs e, string printerName, string processedPath)
    {
        // Use a lock to ensure only one file is processed at a time. This prevents issues
        // if multiple files are dropped into the folder at once.
        lock (_fileLock)
        {
            string filePath = e.FullPath;
            string fileName = Path.GetFileName(filePath);
            string destinationPath = Path.Combine(processedPath, fileName);
            
            Console.WriteLine($"\nNew file detected: {fileName}");

            try
            {
                // Wait for the file to be fully written to disk before trying to read it.
                // This is a common practice to avoid "file in use" errors.
                Thread.Sleep(500); 
                
                // Read and process the file.
                byte[] originalData = File.ReadAllBytes(filePath);
                Console.WriteLine($"Successfully read {originalData.Length} bytes from the file.");

                // Find all occurrences of the ESC ! n command and replace the 'n' parameter.
                byte escByte = 0x1B;
                byte exclamationByte = 0x21;
                byte doubleSizeParameter = 0x30; 

                for (int i = 0; i < originalData.Length - 2; i++)
                {
                    if (originalData[i] == escByte && originalData[i + 1] == exclamationByte)
                    {
                        originalData[i + 2] = doubleSizeParameter;
                        Console.WriteLine($"Replaced ESC ! command at index {i}.");
                    }
                }
                
                if (!originalData.Contains(escByte) || !originalData.Contains(exclamationByte))
                {
                    Console.WriteLine("ESC ! command not found in file. Prepending the command as a fallback.");
                    byte[] commands = { escByte, exclamationByte, doubleSizeParameter };
                    originalData = commands.Concat(originalData).ToArray();
                }

                // Allocate unmanaged memory for the modified data.
                IntPtr pUnmanagedBytes = Marshal.AllocCoTaskMem(originalData.Length);
                Marshal.Copy(originalData, 0, pUnmanagedBytes, originalData.Length);

                // Send the modified data to the printer.
                Console.WriteLine("Sending data to printer...");
                if (RawPrinterHelper.SendBytesToPrinter(printerName, pUnmanagedBytes, originalData.Length))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Successfully sent data to the printer.");
                    Console.ResetColor();
                    
                    // --- MOVE THE FILE TO THE PROCESSED FOLDER ---
                    Console.WriteLine($"Moving file to '{destinationPath}'...");
                    try
                    {
                        File.Move(filePath, destinationPath);
                        Console.WriteLine("File moved successfully.");
                    }
                    catch (Exception moveEx)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Error moving file: {moveEx.Message}");
                        Console.ResetColor();
                    }
                }

                // Free the unmanaged memory.
                Marshal.FreeCoTaskMem(pUnmanagedBytes);
            }
            catch (Win32Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Printing Error (Code: {ex.NativeErrorCode}): {ex.Message}");
                Console.WriteLine("Please check if the printer name is correct, the printer is online, and the driver is installed.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
                Console.ResetColor();
            }
        }
    }
}
