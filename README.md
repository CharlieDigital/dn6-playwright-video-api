# Using Playwright to Capture Web Application Recordings in Docker

Working on [Turas.app](https://turas.app), we recently came across a need to generate video recordings of Turas "stories".

The use case is sharing stories to platforms like Instagram and TikTok.

https://github.com/CharlieDigital/dn6-playwright-video-api/blob/main/e601388e1a79d52c763a4513959bb960.webm.mp4

To achieve this, we can use Playwright's built in recording mechanism; we just need to be able to build an API around it.

We'll walk through creating this from scratch.

## Setting Up

### Create .NET 6.0 Minimal Web API

We'll be using a .NET 6 minimal Web API.

```
mkdir dn6-playwright-video-api
cd dn6-playwright-video-api

dotnet new webapi -minimal
```

Next, replace the default template with the following:

```cs
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// App is listening on 8081
app.Urls.Add("http://0.0.0.0:8081");

app.MapGet("/", () =>
{

});

app.Run();
```

Go ahead and cleanup th `.csproj` file as well since we don't need the default libs:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>dn6_playwright_video_api</RootNamespace>
  </PropertyGroup>
</Project>
```

### Installing Playwright

Use these commands to add Playwright:

```
dotnet add package Microsoft.Playwright

dotnet build

pwsh bin/debug/net6.0/playwright.ps1 install --with-deps chromium
```

Note: we're only going to be using Chromium.

### Installing FFMpegCore

Because Instagram and TikTok do not support WebM as a format, we'll need to convert the output of Playwright into MP4.

To do so:

```
dotnet add package ffmpegcore
```

To run on your local environment, [install the binaries](https://github.com/rosenbjerg/FFMpegCore#binaries).

We'll see later that we can skip this step in the Docker container as it already has `ffmpeg` installed.

## Capturing a Recording

In this use case, we want to hit a Turas.app story URL like: [https://turas.app/s/taiwan/0vylwa7K](https://turas.app/s/taiwan/0vylwa7K).

Once we hit the URL, we want to scroll the page, click a button, and then hover of an element.

To do so, we start the basic setup:

```cs
async Task<string> GenerateVideoAsync()
{
  using var playwright = await Playwright.CreateAsync();

  await using var browser = await playwright.Chromium.LaunchAsync(
    new()
    {
      Headless = true
    });

  await using var context = await browser.NewContextAsync(new()
  {
    ViewportSize = new()
    {
      Width = 430, // This is the relative size of the iPhone 14 Max
      Height = 932
    },
    RecordVideoSize = new()
    {
      Width = 430,
      Height = 932
    },
    RecordVideoDir = "./"
  });

  var page = await context.NewPageAsync();

  await page.GotoAsync("https://turas.app/s/taiwan/0vylwa7K");

  // OMITTED; SEE NEXT SNIPPET
}
```

This code alone will start the recording process.

To interact with the page, we need to drop down to some lower level APIs while recording the video:

```cs
async Task<string> GenerateVideoAsync()
{
  // OMITTED; CODE FROM ABOVE

  // Because our page is an SPA, we want to wait for this element
  // to show up on the DOM before continuing.
  var handle = await page.WaitForSelectorAsync("span#video", new()
  {
    State = WaitForSelectorState.Attached,
    Timeout = 5000
  });

  // Get the other elements we'll be using.
  var outro = await page.QuerySelectorAsync("#outro");
  var personalize = await page.QuerySelectorAsync("#turas-personalize-button");
  var go = await page.QuerySelectorAsync("#turas-personalize-go");

  if (outro != null)
  {
    Console.WriteLine("Selector found; scrolling");

    // Wait 500ms before starting to scroll; looks better this way.
    // If you don't use Task.Delay(), the code will immediately continue.
    await Task.Delay(500);

    var iterations = 0;
    var delta = 7f; // How many pixels to scroll each iteration
    var delay = 20; // How many ms to wait between each iteration

    // To simulate smooth scrolling, we'll scroll in iterations.
    // Play around with the iterations, delta, and delay to get the desired
    // level of smoothness.
    while (iterations < 200)
    {
      await page.Mouse.WheelAsync(0, delta);
      await Task.Delay(delay);
      iterations++;
    }

    // A short pause
    await Task.Delay(250);

    // Click a button on the screen and hove over another button.
    if (personalize != null && go != null)
    {
      await personalize.ClickAsync();
      await Task.Delay(150);
      await go.HoverAsync();
      await Task.Delay(2000);
    }
  }
  else
  {
    Console.WriteLine("Selector not found");
  }

  // This will generate the video.
  await context.CloseAsync();

  // OMITTED; SEE NEXT SECTION
}
```

Finally, we need to convert the default `.webm` into a `.mp4`:

```cs
async Task<string> GenerateVideoAsync()
{
  // OMITTED; CODE FROM ABOVE MANIPULATING THE PAGE

  // Now we convert it to MP4
  var webmPath = await page.Video!.PathAsync();

  using var stream = File.Open(webmPath, FileMode.Open);

  await FFMpegArguments
    .FromPipeInput(new StreamPipeSource(stream))
    .OutputToFile($"{webmPath}.mp4", false, options => options
      .WithVideoCodec(VideoCodec.LibX264)
      // Add other options here.
    )
    .ProcessAsynchronously();

  return $"{webmPath}.mp4";
}
```

To use this function, we simply need to call it from our single route:

```cs
app.MapGet("/", async () =>
{
  Console.WriteLine("Generating video.");

  // Generate the .mp4
  var path = await GenerateVideoAsync();

  // Open it and return the stream as a file.
  var stream = File.Open(path, FileMode.Open);

  return Results.File(
    stream,
    contentType: "video/mp4",
    fileDownloadName: path,
    enableRangeProcessing: true
  );
});
```

> ⚠️ This code does not clean up the files!  You'll definitely want to do that!  I'm going to be storing the files into Google Cloud Storage so I'll delete it after moving it there.

And we'll get the desired output!

## Dockerizing the Workload

Now that you've got it all working, we'll need to Dockerize the workload to get it into a runtime somewhere.

> ⚠️ CAUTION: Because it needs to run the length of time it takes to capture the video, be aware of your costs and how you manage the scaling of this service!

To do so, we ca use a pretty standard Docker file:

```dockerfile
# (1) The build environment
FROM mcr.microsoft.com/dotnet/sdk:6.0-jammy as build
WORKDIR /app

# (2) Copy the .csproj and restore; this will cache these layers so they are not run if no changes.
COPY ./dn6-playwright-video-api.csproj ./dn6-playwright-video-api.csproj
RUN dotnet restore

# (3) Copy the application files and build.
COPY ./Program.cs ./Program.cs
RUN dotnet publish ./dn6-playwright-video-api.csproj -o /app/published-app --configuration Release

# (4) The dotnet tagged Playwright environment includes .NET and ffmpeg
FROM mcr.microsoft.com/playwright/dotnet:v1.34.0-jammy as playwright
WORKDIR /app
COPY --from=build /app/published-app /app

# (5) Start our app!
ENTRYPOINT [ "dotnet", "/app/dn6-playwright-video-api.dll" ]
```

Let's break it down:

1. This is the build layer with the .NET SDK installed.  We are using 6.0 because as far as I'm aware, there's no .NET 7/8 image for Playwright yet.
2. We copy the `.csproj` from the local environment to the image.
3. We copy the source from the local environment to the image.  For more complex setups, it's easier to have a `/src` directory in the local environment and copy the entire directory instead.
4. This is the secret sauce: we use the Microsoft provided Playwright image with Playwright.NET already installed and all of the browsers as well.  As a bonus: it also contains a pre-configured `ffmpeg`!
5. Now our entry point.

You'll want to add environment variables, secrets, etc. as necessary for your use cases.

To build this:

```
docker build . -t dn6-playwright-video-api
```

And to run:

```
docker run -it --rm -p 17775:8081 dn6-playwright-video-api --name dn6-playwright-video-api
```

Now you can open a browser and hit the URL:

```
http://localhost:17775
```

And you get a recording back 🎉