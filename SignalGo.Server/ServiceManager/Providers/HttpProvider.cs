﻿using SignalGo.Server.DataTypes;
using SignalGo.Server.Models;
using SignalGo.Shared.DataTypes;
using SignalGo.Shared.Http;
using SignalGo.Shared.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SignalGo.Server.ServiceManager.Providers
{
    /// <summary>
    /// manage data providing of http and https services
    /// </summary>
    public class HttpProvider : BaseHttpProvider
    {
#if (NET35 || NET40)
        public static void StartToReadingClientData(TcpClient tcpClient, ServerBase serverBase, PipeNetworkStream reader, string headerResponse)
#else
        public static async void StartToReadingClientData(TcpClient tcpClient, ServerBase serverBase, PipeNetworkStream reader, string headerResponse)
#endif
        {
            Console.WriteLine($"Http Client Connected: {((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address.ToString().Replace("::ffff:", "")}");
            ClientInfo client = null;
            try
            {
                while (true)
                {
#if (NET35 || NET40)
                    string line = reader.ReadLine();
#else
                    string line = reader.ReadLine();
#endif
                    headerResponse += line;
                    if (line == "\r\n")
                        break;
                }
#if (NET35 || NET40)
                Task.Factory.StartNew(() =>
#else
                await Task.Run(() =>
#endif
                {
                    if (headerResponse.Contains("Sec-WebSocket-Key"))
                    {
                        client = serverBase.ServerDataProvider.CreateClientInfo(false, tcpClient, reader);
                        client.StreamHelper = SignalGoStreamWebSocket.CurrentWebSocket;
                        string key = headerResponse.Replace("ey:", "`").Split('`')[1].Replace("\r", "").Split('\n')[0].Trim();
                        string acceptKey = AcceptKey(ref key);
                        string newLine = "\r\n";

                        //var response = "HTTP/1.1 101 Switching Protocols" + newLine
                        string response = "HTTP/1.0 101 Switching Protocols" + newLine
                         + "Upgrade: websocket" + newLine
                         + "Connection: Upgrade" + newLine
                         + "Sec-WebSocket-Accept: " + acceptKey + newLine + newLine;
                        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(response);
                        client.ClientStream.Write(bytes);
                        WebSocketProvider.StartToReadingClientData(client, serverBase);
                    }
                    else
                    {

                        try
                        {
                            //serverBase.TaskOfClientInfoes
                            client = serverBase.ServerDataProvider.CreateClientInfo(true, tcpClient, reader);
                            client.StreamHelper = SignalGoStreamBase.CurrentBase;

                            string[] lines = null;
                            if (headerResponse.Contains("\r\n\r\n"))
                                lines = headerResponse.Substring(0, headerResponse.IndexOf("\r\n\r\n")).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            else
                                lines = headerResponse.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            if (lines.Length > 0)
                            {
                                string methodName = GetHttpMethodName(lines[0]);
                                string address = GetHttpAddress(lines[0]);
                                Shared.Http.WebHeaderCollection headers = GetHttpHeaders(lines.Skip(1).ToArray());
                                HandleHttpRequest(methodName, address, serverBase, client, headers);
                            }
                            else
                                serverBase.DisposeClient(client, "HttpProvider StartToReadingClientData no line detected");

                        }
                        catch
                        {
                            serverBase.DisposeClient(client, "HttpProvider StartToReadingClientData exception");
                        }
                    }
                });
            }
            catch// (Exception ex)
            {
                //if (client != null)
                //serverBase.AutoLogger.LogError(ex, $"{client.IPAddress} {client.ClientId} ServerBase HttpProvider StartToReadingClientData");
                serverBase.DisposeClient(client, "HttpProvider StartToReadingClientData exception 2");
            }
        }


        /// <summary>
        /// Accept key for websoket client
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private static string AcceptKey(ref string key)
        {
            string longKey = key + _guid;
            byte[] hashBytes = ComputeHash(longKey);
            return Convert.ToBase64String(hashBytes);
        }

        private static SHA1 _sha1 = SHA1.Create();
        /// <summary>
        /// Compute sha1 hash
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        private static byte[] ComputeHash(string str)
        {
            return _sha1.ComputeHash(Encoding.ASCII.GetBytes(str));
        }

        /// <summary>
        /// get method name of http response
        /// </summary>
        /// <param name="reponse">response string</param>
        /// <returns>method name like "GET"</returns>
        private static string GetHttpMethodName(string reponse)
        {
            string[] lines = reponse.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0)
                return lines[0];
            return "";
        }

        /// <summary>
        /// get http address from response
        /// </summary>
        /// <param name="reponse">response string</param>
        /// <returns>address</returns>
        private static string GetHttpAddress(string reponse)
        {
            string[] lines = reponse.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 1)
                return lines[1];
            return "";
        }

        /// <summary>
        /// get http header from response
        /// </summary>
        /// <param name="lines">lines of headers</param>
        /// <returns>http headers</returns>
        private static Shared.Http.WebHeaderCollection GetHttpHeaders(string[] lines)
        {
            Shared.Http.WebHeaderCollection result = new Shared.Http.WebHeaderCollection();
            foreach (string item in lines)
            {
                string[] keyValues = item.Split(new[] { ':' }, 2);
                if (keyValues.Length > 1)
                {
                    result.Add(keyValues[0], keyValues[1].TrimStart());
                }
            }
            return result;
        }



        private bool IsMethodInfoOfJsonParameters(IEnumerable<MethodInfo> methods, List<string> names)
        {
            bool isFind = false;
            foreach (MethodInfo method in methods)
            {
                int fakeParameterCount = 0;
                int findCount = method.GetCustomAttributes<FakeParameterAttribute>().Count();
                fakeParameterCount += findCount;
                if (method.GetParameters().Length == names.Count - fakeParameterCount)
                {
                    for (int i = 0; i < fakeParameterCount; i++)
                    {
                        if (names.Count > 0)
                            names.RemoveAt(names.Count - 1);
                    }
                }
                if (method.GetParameters().Count(x => names.Any(y => y.ToLower() == x.Name.ToLower())) == names.Count)
                {
                    isFind = true;
                    break;
                }
            }
            return isFind;
        }


    }
}
