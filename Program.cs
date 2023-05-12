using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Xml;

namespace adaptec_prometheus;

internal class Program
{
    static string arcconf_path = "/usr/local/bin/arcconf";
    delegate void LineFunc(string param, string value);
    static int Main(string[] args)
    {
        return GetMainInfo() | GetSmartInfo();
    }
    static MemoryStream? GetArcconfOutput(string args)
    {
        Process process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = arcconf_path,
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
            System.Console.Error.WriteLine("Failed to start arcconf: " + ex.Message);
            return null;
        }
        bool exited = process.WaitForExit(30000);
        if (!exited)
        {
            System.Console.Error.WriteLine("arcconf timed out");
            return null;
        }
        if (process.ExitCode != 0)
        {
            System.Console.Error.WriteLine("arcconf exited with code {0}", process.ExitCode);
            return null;
        }
        return new MemoryStream(Encoding.UTF8.GetBytes(process.StandardOutput.ReadToEnd().ToArray()));
    }
    static int GetMainInfo()
    {
        MemoryStream? arcconf_output = GetArcconfOutput("getconfig 1");
        if (arcconf_output == null)
        {
            System.Console.WriteLine("adaptec_raid_is_optimal 0");
            System.Console.Error.WriteLine("Failed to get arcconf getconfig output");
            return 1;
        }
        else
        {
            StreamReader arcconf_reader = new StreamReader(arcconf_output);
            while (!arcconf_reader.EndOfStream)
            {
                string line = arcconf_reader.ReadLine() ?? "";
                switch (line.Trim())
                {
                    case "Controller information":
                        ProcessSection(arcconf_reader, ProcessControllerInfo);
                        break;
                    case "RAID Properties":
                        ProcessSection(arcconf_reader, ProcessLDInfo);
                        break;
                    case "Controller Battery Information":
                        ProcessSection(arcconf_reader, ProcessBatteryInfo);
                        break;
                }
            }
            arcconf_reader.Close();
            arcconf_output.Close();
            return 0;
        }
    }
    static void ProcessSection(StreamReader output, LineFunc linefunc)
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
        return;
    }
    static void ProcessControllerInfo(string param, string value)
    {
        switch (param)
        {
            case "Controller Status":
                System.Console.WriteLine("adaptec_raid_is_optimal {0}", (value == "Optimal") ? 1 : 0);
                break;
            case "Temperature":
                System.Console.WriteLine("adaptec_raid_temperature {0}", value.Trim().Split(' ')[0]);
                break;
            case "Defunct disk drive count":
                System.Console.WriteLine("adaptec_raid_defunct_drives {0}", value.Trim());
                break;
        }
        return;
    }
    static void ProcessLDInfo(string param, string value)
    {
        switch (param)
        {
            case "Logical devices/Failed/Degraded":
                System.Console.WriteLine("adaptec_raid_ld_total {0}", value.Split('/')[0]);
                System.Console.WriteLine("adaptec_raid_ld_failed {0}", value.Split('/')[1]);
                System.Console.WriteLine("adaptec_raid_ld_degraded {0}", value.Split('/')[2]);
                break;
        }
        return;
    }
    static void ProcessBatteryInfo(string param, string value)
    {
        switch (param)
        {
            case "Status":
                System.Console.WriteLine("adaptec_raid_battery_is_optimal {0}", (value == "Optimal") ? 1 : 0);
                break;
            case "Over temperature":
                System.Console.WriteLine("adaptec_raid_battery_temp_is_ok {0}", (value == "No") ? 1 : 0);
                break;
        }
        return;
    }
    static int GetSmartInfo()
    {
        MemoryStream? arcconf_output = GetArcconfOutput("getsmartstats 1");
        if (arcconf_output == null)
        {
            System.Console.Error.WriteLine("Failed to get arcconf getsmartstats output");
            return 1;
        }
        else
        {
            StreamReader arcconf_reader = new StreamReader(arcconf_output);
            while (!arcconf_reader.EndOfStream)
            {
                XmlReaderSettings xmlreadersettings = new XmlReaderSettings();
                xmlreadersettings.ConformanceLevel = ConformanceLevel.Fragment;
                xmlreadersettings.DtdProcessing = DtdProcessing.Prohibit;
                XmlReader reader;
                try
                {
                    reader = XmlReader.Create(arcconf_reader, xmlreadersettings);
                }
                catch (Exception ex)
                {
                    System.Console.Error.WriteLine("Failed to create XML reader: " + ex.Message);
                    return 1;
                }
                while (reader.Read())
                {
                    if ((reader.Name == "PhysicalDriveSmartStats") && reader.IsStartElement())
                    {
                        ProcessDriveSmartInfo(reader, reader["id"]);
                    }
                }
                reader.Close();
            }
            arcconf_reader.Close();
            arcconf_output.Close();
            return 0;
        }
    }
    static void ProcessDriveSmartInfo(XmlReader reader, string? drive)
    {
        while (reader.Read() && reader.Name != "PhysicalDriveSmartStats")
        {
            if (reader.Name == "Attribute")
            {
                Console.WriteLine("adaptec_smart_attribute{{code=\"{0}\",name=\"{1}\",drive=\"{2}\"}} {3}", reader["id"], reader["name"], drive ?? "", reader["rawValue"]);
            }
        }
        return;
    }
}