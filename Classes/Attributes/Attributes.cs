﻿using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;

namespace Hi3Helper.Http
{
    public partial class HttpNew
    {
        // Inner HttpClient instance
        private HttpClient _client;
        // Inner HttpClient handler
        private HttpClientHandler _handler;
        // Inner Buffer size
        private readonly int _bufferSize = 4 << 20;

        // Max allowed Connections for HttpClient instance
        private readonly byte ConnectionMax = 16;
        // Max allowed Sessions for HttpClient instance (in Multi-session mode)
        private readonly byte ConnectionSessionsMax = 8;
        // Sessions count
        private byte ConnectionSessions;
        // Connection Token for Cancellation
        private CancellationToken ConnectionToken;

        // Max Retry Count
        private byte RetryMax = 5;
        // Retry Interval (in milliseconds)
        private short RetryInterval = 1000;

        // Sessions list
        private List<Session> Sessions = new List<Session>();
        private Stopwatch SessionsStopwatch;
        public bool IsDownloadContinue = false;

        // Path of the Download
        private string PathURL;
        private string PathOutput;
        private bool PathOverwrite;

        // Download Statistics
        private AttributesSize SizeAttribute;

        // This is for Multisession mode only
        public MultisessionState DownloadState;

        public void ResetState()
        {
            this.SessionsStopwatch = Stopwatch.StartNew();
            this.Sessions.Clear();
            this._client = new HttpClient(this._handler);
        }
    }
}