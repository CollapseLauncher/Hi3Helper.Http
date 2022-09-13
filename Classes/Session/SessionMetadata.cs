using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace Hi3Helper.Http
{
    public partial class HttpNew
    {
        private void CreateOrUpdateMetadata()
        {
            FileInfo Metadata = new FileInfo(this.PathOutput + ".mdat");
            using (BinaryWriter Writer = new BinaryWriter(Metadata.Create()))
            {
                // For Sessions Count
                Writer.Write(this.ConnectionSessions);
                // For Total Size
                Writer.Write(this.SizeTotal);
                // For Ovewrite-able Check
                Writer.Write(this.PathOverwrite);
                for (int i = 0; i < this.ConnectionSessions; i++)
                {
                    // For Checksum Hash
                    Writer.Write((int)0);
                    // For Checksum Pos
                    Writer.Write((long)0);
                    // For OffsetStart
                    Writer.Write(this.Sessions[i].OffsetStart ?? 0);
                    // For OffsetEnd
                    Writer.Write(this.Sessions[i].OffsetEnd ?? 0);
                    // For Session Size
                    Writer.Write((this.Sessions[i].OffsetEnd - this.Sessions[i].OffsetStart) ?? 0);
                    // For IsCompleted
                    Writer.Write(false);
                    // For IsLastSession
                    Writer.Write(this.Sessions[i].IsLastSession);
                }
            }
        }

        private bool LoadMetadata()
        {
            FileInfo Metadata = new FileInfo(this.PathOutput + ".mdat");
            if (!Metadata.Exists) return false;

            this.IsSessionContinue = true;
            Session LastSession;

            // For Last Checksum Hash
            int LastChecksum;
            // For Last Checksum Pos
            long LastChecksumPos;
            // For Last OffsetStart
            long OffsetStart;
            // For Last OffsetEnd
            long OffsetEnd;
            // For Session Size
            long SessionSize;
            // For Last IsCompleted
            bool IsCompleted;
            // For Last IsLastSession
            bool IsLastSession;

            using (BinaryReader Reader = new BinaryReader(Metadata.OpenRead()))
            {
                this.ConnectionSessions = Reader.ReadByte();
                this.SizeTotal = Reader.ReadInt64();
                this.PathOverwrite = Reader.ReadBoolean();
                for (int i = 0; i < this.ConnectionSessions; i++)
                {
                    LastChecksum = Reader.ReadInt32();
                    LastChecksumPos = Reader.ReadInt64();
                    OffsetStart = Reader.ReadInt64();
                    OffsetEnd = Reader.ReadInt64();
                    SessionSize = Reader.ReadInt64();
                    IsCompleted = Reader.ReadBoolean();
                    IsLastSession = Reader.ReadBoolean();

                    LastSession = new Session(
                        this.PathURL, this.PathOutput, null,
                        this.ConnectionToken, true, true,
                        OffsetStart, OffsetEnd, this.PathOverwrite
                        )
                    {
                        IsLastSession = IsLastSession,
                        IsCompleted = IsCompleted,
                        SessionSize = SessionSize
                    };

                    LastSession.LoadLastHash(LastChecksum);
                    LastSession.LoadLastHashPos(LastChecksumPos);

                    this.Sessions.Add(LastSession);
                }
            }

            return true;
        }
    }
}
