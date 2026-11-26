using System;
using System.Diagnostics;
using System.IO;

class Program
{
    const string StateFilePath = "state.txt";

    static void Main(string[] args)
    {
        
        
        //RunTicCmd("--step-mode 4");
        //RunTicCmd("--position 400");

        if (args.Length >= 0)
        {
            // args[0] contains the intent, need to check the current state

            string currentstate = ReadTextValue();
            
            
            if (args[0] == "set")
            {
                RunTicCmd("--energize");
                RunTicCmd("--resume");
                RunTicCmd("--reset");
                RunTicCmd("--deenergize");
                StoreTextValue("closed");
            }

            if (args[0] == "close")
            {
                if (currentstate == "closed")
                {
                    Console.WriteLine("Already closed");                 
                    return;
                }

                RunTicCmd("--energize");
                RunTicCmd("--resume");
                RunTicCmd("--position 0"); // closed is home/0
                StoreTextValue("closed");
                RunTicCmd("--exit-safe-start");
                System.Threading.Thread.Sleep(15000);
                RunTicCmd("--deenergize");

                SaveEventFile("Arm Closed");

            }
            else if (args[0] == "open" )
            {
                if (currentstate == "open")
                {
                    Console.WriteLine("Already open");
                    return;
                }

                RunTicCmd("--energize");
                RunTicCmd("--resume");
                RunTicCmd("--position 1500");  // 1 turn = 200  15 turns = 3000
                StoreTextValue("open");
                RunTicCmd("--exit-safe-start");
                System.Threading.Thread.Sleep(15000);
                RunTicCmd("--deenergize");

                SaveEventFile("Arm Opened");
            }
        }
                
        Console.WriteLine(args[0]);
        return;
        

        // Example: Energize the motor, exit safe start, and move to position 400
    }

    static void SaveEventFile(string Notation)
    {
        try
        {


            string sourceDir = Directory.GetCurrentDirectory();
            string eventsDir = Path.Combine(sourceDir, "events");
            Directory.CreateDirectory(eventsDir);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HH_mm_ss");
            string extension = ".txt";
            string targetPath = Path.Combine(eventsDir, $"{timestamp} {Notation}{extension}");

            File.WriteAllText(targetPath, Notation);

            Console.WriteLine($"Saved event image: {targetPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving event file: {ex.Message}");
        }
    }


    static void StoreTextValue(string value)
    {
        File.WriteAllText(StateFilePath, value);
    }

    static string ReadTextValue()
    {
        if (!File.Exists(StateFilePath))
        {
            return string.Empty;
        }

        return File.ReadAllText(StateFilePath);
    }

    static void RunTicCmd(string arguments)
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "ticcmd", // Ensure ticcmd is added to your system PATH
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"Error running ticcmd {arguments}: {error}");
                }
                else if (!string.IsNullOrEmpty(output))
                {
                    Console.WriteLine(output);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
        }
    }
}