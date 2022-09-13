﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Hi3Helper.Http
{
    public partial class HttpNew
    {
        public bool IOMultiVerify(Session Input)
        {
            // Initialize local checksum
            SimpleChecksum Checksum = new SimpleChecksum();
            byte[] Buffer = new byte[_bufferSize];
            int Read;
            int NextRead = Buffer.Length;

            // Set Session State to verification check
            Input.SessionState = MultisessionState.CheckingLastSessionIntegrity;

            // Set Output Stream position to beginning
            Input.StreamOutput.Position = 0;
            // Read Stream into Buffer
            while ((Read = Input.StreamOutput.Read(Buffer, 0, NextRead)) > 0)
            {
                // Calculate Next Read Jump
                NextRead = (int)Math.Min(Buffer.Length, Input.LastChecksumPos - Input.StreamOutput.Position);
                // Compute checksum from Buffer
                Checksum.ComputeHash32(Buffer, Read);
                // Throw if Token Cancellation is requested
                Input.SessionToken.ThrowIfCancellationRequested();
            }

            Input.SessionState = MultisessionState.CompleteLastSessionIntegrity;

            return Checksum.Hash32 == Input.LastChecksumHash;
        }

        public void IOMulti(Session Input)
        {
            byte[] Buffer = new byte[_bufferSize];
            int Read;

            // Reset session retry attempt
            Input.SessionRetryAttempt = 1;
            // Read Stream into Buffer
            while ((Read = Input.StreamInput.Read(Buffer, 0, Buffer.Length)) > 0)
            {
                // Write Buffer to the output Stream
                Input.StreamOutput.Write(Buffer, 0, Read);
                // Compute checksum from Buffer
                Input.Checksum.ComputeHash32(Buffer, Read);
                // Set Inner Session Status
                Input.SessionState = MultisessionState.Downloading;
                // Throw if Token Cancellation is requested
                Input.SessionToken.ThrowIfCancellationRequested();
            }

            Input.IsCompleted = true;
        }
    }
}
