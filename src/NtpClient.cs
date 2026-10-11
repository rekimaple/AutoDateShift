// NTP 网络校时（SNTP 客户端，UDP 123，仅用户主动勾选"网络校时"时使用）
using System;
using System.Net;
using System.Net.Sockets;

namespace AutoDateShift
{
    internal static class NtpClient
    {
        /// <summary>从 NTP 服务器获取当前网络时间（本地时区）。失败抛异常。</summary>
        public static DateTime GetNetworkTime(string server, int timeoutMs)
        {
            var data = new byte[48];
            data[0] = 0x1B; // LI=0, VN=3, Mode=3（客户端）

            IPAddress addr = null;
            foreach (var a in Dns.GetHostAddresses(server))
                if (a.AddressFamily == AddressFamily.InterNetwork) { addr = a; break; }
            if (addr == null) throw new Exception("无法解析 NTP 服务器地址: " + server);

            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.ReceiveTimeout = timeoutMs;
                socket.SendTimeout = timeoutMs;
                socket.Connect(new IPEndPoint(addr, 123));
                socket.Send(data);
                socket.Receive(data);
            }

            ulong intPart = ((ulong)data[40] << 24) | ((ulong)data[41] << 16) | ((ulong)data[42] << 8) | data[43];
            ulong fractPart = ((ulong)data[44] << 24) | ((ulong)data[45] << 16) | ((ulong)data[46] << 8) | data[47];
            ulong ms = intPart * 1000 + (fractPart * 1000) / 0x100000000UL;
            var utc = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds((long)ms);
            return utc.ToLocalTime();
        }
    }
}
