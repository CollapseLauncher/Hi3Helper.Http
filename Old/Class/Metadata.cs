using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Hi3Helper.Http
{
    public class MetadataProp
    {
        public byte ChunkSize;
        public long RemoteFileSize;
        public bool CanOverwrite;
        public long LastOffset;
        public byte[] LastCRC;
        public MetadataProp[] ChunkProp;
    }

    public partial class Http
    {
        private void CreateMetadataFile(string PathOut, MetadataProp Metadata)
        {
            FileInfo file = new FileInfo(PathOut);
            if (file.Exists && Metadata.CanOverwrite)
                File.Delete(file.FullName);

            if (file.Exists && !Metadata.CanOverwrite && file.Length == Metadata.RemoteFileSize)
                throw new FileLoadException("File is already downloaded! Please consider to delete or move the existing file first.");

            using (BinaryWriter Writer = new BinaryWriter(new FileStream(PathOut + ".h3mtd", FileMode.Create, FileAccess.Write)))
            {
                Writer.Write(Metadata.ChunkSize);
                Writer.Write(Metadata.RemoteFileSize);
                Writer.Write(Metadata.CanOverwrite);
                for (int i = 0; i < Metadata.ChunkSize; i++)
                {
                    Writer.Write((long)0);
                    Writer.Write(new byte[4] {0, 0, 0, 0});
                }
            }
        }

        private void UpdateMetadataFile(string PathOut, MetadataProp Metadata, in IList<SessionAttribute> Session)
        {
            FileInfo file = new FileInfo(PathOut + ".h3mtd");
            using (BinaryWriter Writer = new BinaryWriter(file.OpenWrite()))
            {
                Writer.Write(Metadata.ChunkSize);
                Writer.Write(Metadata.RemoteFileSize);
                Writer.Write(Metadata.CanOverwrite);
                for (int i = 0; i < Metadata.ChunkSize; i++)
                {
                    Writer.Write(Session[i].OutSize);
                    Writer.Write(Session[i].LastCRC);
                }
            }
        }

        private MetadataProp ReadMetadataFile(string PathOut)
        {
            MetadataProp ret = new MetadataProp();
            FileInfo file = new FileInfo(PathOut + ".h3mtd");

            if (!file.Exists)
                throw new HttpHelperSessionMetadataNotExist(string.Format("Metadata for \"{0}\" doesn't exist", PathOut));

            using (BinaryReader Reader = new BinaryReader(file.OpenRead()))
            {
                ret.ChunkSize = Reader.ReadByte();
                ret.RemoteFileSize = Reader.ReadInt64();
                ret.CanOverwrite = Reader.ReadBoolean();
                ret.ChunkProp = new MetadataProp[ret.ChunkSize];
                for (int i = 0; i < ret.ChunkSize; i++)
                {
                    ret.ChunkProp[i] = new MetadataProp();
                    ret.ChunkProp[i].LastOffset = Reader.ReadInt64();
                    ret.ChunkProp[i].LastCRC = Reader.ReadBytes(4);
                }
            }

            return ret;
        }
    }
}
