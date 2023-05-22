using System.Diagnostics;
using System.Text;
using System.Xml;

namespace adaptec_prometheus;

internal static class Program
{
    private const string _arcconfPath = "/usr/local/bin/arcconf";

    private delegate void LineFunc(string param, string value);

    private static int Main()
    {
        return GetMainInfo() | GetSmartInfo();
    }

    private static MemoryStream? GetArcconfOutput(string args)
    {
        Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _arcconfPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                RedirectStandardError = true
            }
        };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Failed to start arcconf: " + ex.Message);
            return null;
        }
        bool exited = process.WaitForExit(30000);
        if (!exited)
        {
            Console.Error.WriteLine("arcconf timed out");
            return null;
        }

        if (process.ExitCode == 0)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(process.StandardOutput.ReadToEnd().ToArray()));
        }
        Console.Error.WriteLine("arcconf exited with code {0}", process.ExitCode);
        return null;
    }

    private static int GetMainInfo()
    {
        MemoryStream? arcconfOutput = GetArcconfOutput("getconfig 1");
        if (arcconfOutput == null)
        {
            Console.WriteLine("adaptec_raid_is_optimal 0");
            Console.Error.WriteLine("Failed to get arcconf getconfig output");
            return 1;
        }

        StreamReader arcconfReader = new(arcconfOutput);
        while (!arcconfReader.EndOfStream)
        {
            string line = arcconfReader.ReadLine() ?? "";
            switch (line.Trim())
            {
                case "Controller information":
                    ProcessSection(arcconfReader, ProcessControllerInfo);
                    break;
                case "RAID Properties":
                    ProcessSection(arcconfReader, ProcessLdInfo);
                    break;
                case "Controller Battery Information":
                    ProcessSection(arcconfReader, ProcessBatteryInfo);
                    break;
            }
        }
        arcconfReader.Close();
        arcconfOutput.Close();
        return 0;
    }

    private static void ProcessSection(TextReader output, LineFunc linefunc)
    {
        output.ReadLine();
        string line;
        do
        {
            line = output.ReadLine() ?? "";
            if (line.Contains(':'))
            {
                linefunc(line.Trim().Split(':')[0].Trim(), line.Trim().Split(':')[1].Trim());
            }
        } while (!line.Trim().StartsWith("---") && line != "");
    }

    private static void ProcessControllerInfo(string param, string value)
    {
        switch (param)
        {
            case "Controller Status":
                Console.WriteLine("adaptec_raid_is_optimal {0}", value == "Optimal" ? 1 : 0);
                break;
            case "Temperature":
                Console.WriteLine("adaptec_raid_temperature {0}", value.Trim().Split(' ')[0]);
                break;
            case "Defunct disk drive count":
                Console.WriteLine("adaptec_raid_defunct_drives {0}", value.Trim());
                break;
        }
    }

    private static void ProcessLdInfo(string param, string value)
    {
        switch (param)
        {
            case "Logical devices/Failed/Degraded":
                Console.WriteLine("adaptec_raid_ld_total {0}", value.Split('/')[0]);
                Console.WriteLine("adaptec_raid_ld_failed {0}", value.Split('/')[1]);
                Console.WriteLine("adaptec_raid_ld_degraded {0}", value.Split('/')[2]);
                break;
        }
    }

    private static void ProcessBatteryInfo(string param, string value)
    {
        switch (param)
        {
            case "Status":
                Console.WriteLine("adaptec_raid_battery_is_optimal {0}", value == "Optimal" ? 1 : 0);
                break;
            case "Over temperature":
                Console.WriteLine("adaptec_raid_battery_temp_is_ok {0}", value == "No" ? 1 : 0);
                break;
        }
    }

    private static int GetSmartInfo()
    {
        MemoryStream? arcconfOutput = GetArcconfOutput("getsmartstats 1");
        if (arcconfOutput == null)
        {
            Console.Error.WriteLine("Failed to get arcconf getsmartstats output");
            return 1;
        }

        StreamReader arcconfReader = new(arcconfOutput);
        XmlReaderSettings xmlreadersettings = new()
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            DtdProcessing = DtdProcessing.Prohibit
        };
        XmlReader reader;
        try
        {
            reader = XmlReader.Create(arcconfReader, xmlreadersettings);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Failed to create XML reader: " + ex.Message);
            arcconfReader.Close();
            arcconfOutput.Close();
            return 1;
        }
        while (reader.Read())
        {
            if (reader.Name == "PhysicalDriveSmartStats" && reader.IsStartElement())
            {
                ProcessDriveSmartInfo(reader, reader["id"]);
            }
        }
        reader.Close();
        arcconfReader.Close();
        arcconfOutput.Close();
        return 0;
    }

    private static void ProcessDriveSmartInfo(XmlReader reader, string? drive)
    {
        while (reader.Read() && reader.Name != "PhysicalDriveSmartStats")
        {
            if (reader.Name == "Attribute")
            {
                Console.WriteLine("adaptec_smart_attribute{{code=\"{0}\",name=\"{1}\",drive=\"{2}\"}} {3}", reader["id"], reader["name"], drive ?? "", reader["rawValue"]);
            }
        }
    }
}