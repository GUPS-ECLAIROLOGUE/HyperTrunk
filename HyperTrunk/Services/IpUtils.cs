using System;
using System.Linq;
using System.Net;

namespace HyperTrunk.Services
{
    // Petits calculs réseau purs (aucun appel Hyper-V ici), testables sans matériel.
    public static class IpUtils
    {
        public static int MaskToPrefixLength(string subnetMask)
        {
            int prefix = 0;
            foreach (string octetText in subnetMask.Split('.'))
            {
                int octet = int.Parse(octetText);
                while (octet > 0)
                {
                    prefix += octet & 1;
                    octet >>= 1;
                }
            }
            return prefix;
        }

        public static string PrefixLengthToMask(int prefixLength)
        {
            uint mask = prefixLength <= 0 ? 0u : 0xFFFFFFFFu << (32 - prefixLength);
            byte[] bytes = BitConverter.GetBytes(mask);
            Array.Reverse(bytes);
            return new IPAddress(bytes).ToString();
        }

        public static string NormalizeIp(string ip)
        {
            string[] octets = ip.Split('.');
            if (octets.Length != 4) return ip;

            return string.Join(".", octets.Select(o => int.Parse(o).ToString()));
        }
    }
}
