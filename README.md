# Hi3HelperCore.Http
 Http downloader wrapper with Multi-Session support

## Usage
### Single-session
```C#
Http client = new Http();
await client.Download("http://yourURL", "C:\yourOutputData");
```

### Single-session using ``Stream``s
```C#
using (MemoryStream stream = new MemoryStream())
{
    Http client = new Http();
    await client.Download("http://yourURL", stream);
    
    // Doing something with the stream here
}
```

### Multi-session
```C#
int Session = 4;
Http client = new Http();
await client.DownloadMultisession("http://yourURL", "C:\yourOutputData", Session);
await client.MergeMultisession("C:\yourOutputData");
```

### Using ``DownloadProgress`` event to display download progress
#### In your method
```C#
public static async Task Main()
{
    Http client = new Http();
    client.DownloadProgress += YourProgress;
    // Can be used with DownloadMultisession() as well
    await client.Download("http://yourURL", "C:\yourOutputData");
    await client.DownloadProgress -= YourProgress;
}
```
#### In your ``YourProgress`` event method
```C#
private static void YourProgress(object? sender, DownloadEvent e)
{
    Console.Write("\r{0}%", e.ProgressPercentage);
}
```

Other usages will be published soon.
