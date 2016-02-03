using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;

namespace SetEnvironment
{
    class Program
    {
        static string localPrefix = "192.168.";

        static string[] s_dns = new string[]
        {
            "192.168.0.4",
            "202.96.128.68",
            "202.96.134.133",
        };

        static string[] s_wins = new string[2]
        {
            "192.168.0.4",
            "",
        };

        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
        private static extern UInt32 DnsFlushResolverCache();

        static void SetNetworkAdapter()
        {
            var mc = new ManagementClass("Win32_NetworkAdapterConfiguration");
            var moc = mc.GetInstances();
            foreach (ManagementObject mo in moc)
            {
                if (!(bool)mo["IPEnabled"])
                {
                    continue;
                }

                var caption = mo["Caption"] as string;
                var ipAddress = mo["IPAddress"] as string[];
                var defaultIPGateway = mo["DefaultIPGateway"] as string[];

                bool isLocal = false;
                if (defaultIPGateway != null)
                {
                    for (int i = 0; i < ipAddress.Count(); ++i)
                    {
                        if (ipAddress[i].Contains(localPrefix) &&
                            !String.IsNullOrWhiteSpace(defaultIPGateway[i]))
                        {
                            isLocal = true;
                            break;
                        }
                    }
                }

                if (isLocal)
                {
                    // 设置 DNS
                    Console.WriteLine("Setting {0} DNS ......", caption);
                    string method = "SetDNSServerSearchOrder";
                    var inParameters = mo.GetMethodParameters(method);
                    inParameters["DNSServerSearchOrder"] = s_dns;
                    var outParameters = mo.InvokeMethod(method, inParameters, null);

                    // 设置 WINS
                    Console.WriteLine("Setting {0} WINS ......", caption);
                    method = "SetWINSServer";
                    inParameters = mo.GetMethodParameters(method);
                    inParameters["WINSPrimaryServer"] = s_wins[0];
                    inParameters["WINSSecondaryServer"] = s_wins[1];
                    outParameters = mo.InvokeMethod(method, inParameters, null);
                }
            }

            Console.WriteLine("Flush DNS ......");
            DnsFlushResolverCache();
        }

        struct Host
        {
            public IPAddress ip;
            public string name;
            public bool processed;

            public Host(string ip, string name)
            {
                this.ip = IPAddress.Parse(ip);
                this.name = name;
                processed = false;
            }

            public string GetHostLine()
            {
                return String.Format("{0}\t{1}", ip, name);
            }
        }

        static Host[] s_hosts = new Host[]
        {
            new Host("192.168.0.32", "metal.test.com"),
            new Host("192.168.0.32", "track.test.com"),
        };

        static void ModifyHostsFile()
        {
            var hostsFileName = Path.Combine(Environment.SystemDirectory, "drivers/etc/hosts");
            Console.WriteLine("Reading {0} ......", hostsFileName);
            var contents = new List<string>();
            contents.AddRange(File.ReadAllLines(hostsFileName));

            bool isModified = false;

            for (int i = 0; i < contents.Count(); ++i)
            {
                var lower = contents[i].ToLowerInvariant();
                if (!String.IsNullOrWhiteSpace(lower))
                {
                    for (int j = 0; j < s_hosts.Count(); ++j)
                    {
                        if (lower.Contains(s_hosts[j].name))
                        {
                            var line = s_hosts[j].GetHostLine();
                            if (contents[i] != line)
                            {
                                contents[i] = line;
                                Console.WriteLine(line);
                                isModified = true;
                            }
                            s_hosts[j].processed = true;
                            break;
                        }
                    }
                }
            }

            foreach (var host in s_hosts.Where(h => !h.processed))
            {
                var line = host.GetHostLine();
                contents.Add(line);
                Console.WriteLine(line);
                isModified = true;
            }

            if (isModified)
            {
                var tempFileName = Path.GetTempFileName();
                File.WriteAllLines(tempFileName, contents);
                Console.WriteLine("Writing {0} ......", hostsFileName);
                File.Delete(hostsFileName);
                File.Move(tempFileName, hostsFileName);
            }
        }

        static void Main(string[] args)
        {
            SetNetworkAdapter();
            ModifyHostsFile();

            Console.WriteLine("Process finished.");
#if DEBUG
            Console.ReadLine();
#endif
        }
    }
}
