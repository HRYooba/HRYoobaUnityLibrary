using System;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UniRx;

namespace HRYooba.Library.Network
{
    public class UnityTcpServer : IDisposable
    {
        private const int BufferSize = 1024;

        private TcpListener _listener;
        private List<UnityTcpSession> _sessions = new List<UnityTcpSession>();
        private CancellationTokenSource _cancellation;
        
        private Subject<UnityTcpSession> _onSessionConnected = new Subject<UnityTcpSession>();
        private Subject<UnityTcpSession> _onSessionDisconnected = new Subject<UnityTcpSession>();
        private Subject<string> _onMessageReceived = new Subject<string>();

        public UnityTcpServer() { }

        ~UnityTcpServer()
        {
            Dispose();
        }

        public IObservable<UnityTcpSession> OnSessionConnected
        {
            get
            {
                return _onSessionConnected.ObserveOnMainThread();
            }
        }

        public IObservable<UnityTcpSession> OnSessionDisconnected
        {
            get
            {
                return _onSessionDisconnected.ObserveOnMainThread();
            }
        }

        public IObservable<string> OnMessageReceived
        {
            get { return _onMessageReceived.ObserveOnMainThread(); }
        }

        public void Open(int port)
        {
            Debug.Log($"UnityTcpServer port({port}) open.");

            var localEndPoint = new IPEndPoint(IPAddress.Any, port);
            _listener = new TcpListener(localEndPoint);
            _listener.Start();

            _cancellation = new CancellationTokenSource();

            // 別スレッドで接続待機を行う
            Task.Run(() => Listen(_cancellation.Token));
        }

        public void Close()
        {
            if (_listener != null)
            {
                var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                Debug.Log($"UnityTcpServer port({port}) close.");
            }

            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;

            _listener?.Stop();
            _listener = null;

            foreach (var session in _sessions)
            {
                session.Dispose();
            }
            _sessions.Clear();
        }

        public void Send(string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message + "\n");

            foreach (var session in _sessions)
            {
                var stream = session.Client.GetStream();
                stream.Write(buffer, 0, buffer.Length);
            }
        }

        public void Send(Guid id, string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message + "\n");

            try
            {
                var session = _sessions.Where(_ => _.Id == id).First();
                var stream = session.Client.GetStream();
                stream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public void Dispose()
        {
            Close();

            _sessions = null;

            _onSessionConnected.Dispose();
            _onSessionConnected = null;

            _onSessionDisconnected.Dispose();
            _onSessionDisconnected = null;

            _onMessageReceived.Dispose();
            _onMessageReceived = null;
        }

        private async Task Listen(CancellationToken cancellationToken)
        {
            // クライアントの接続を常に待機
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    var session = new UnityTcpSession(client);
                    _sessions.Add(session);
                    _onSessionConnected.OnNext(session);

                    // 別スレッドでデータの受け取りを行う
                    var task = Task.Run(() => Receive(session, cancellationToken));
                }
                catch (Exception ex)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Debug.LogException(ex);
                }
            }
        }

        private async Task Receive(UnityTcpSession session, CancellationToken cancellationToken)
        {
            // データ受け取り
            try
            {
                var stream = session.Client.GetStream();
                var message = new StringBuilder();

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var buffer = new byte[BufferSize];
                    var bytesRead = await stream.ReadAsync(buffer, 0, BufferSize);

                    if (bytesRead > 0)
                    {
                        message.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                    }
                    else
                    {
                        // セッション切断
                        _onSessionDisconnected.OnNext(session);
                        _sessions.Remove(session);
                        session.Dispose();
                        break;
                    }

                    // データ終了文字があれば読み取り完了
                    if (message.ToString().Contains("\n"))
                    {
                        _onMessageReceived.OnNext(message.Replace("\n", "").ToString());
                        message = null; // リソース解放

                        // 再度データ受け取り待ちに
                        var task = Task.Run(() => Receive(session, cancellationToken));

                        // データ受け取りスレッド終了
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Debug.LogException(ex);
            }
        }
    }
}