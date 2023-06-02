using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using Microsoft.Playwright;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.Urls.Add("http://0.0.0.0:8081");

Console.WriteLine("Starting...");

/// <summary>
/// Our API entry point.
/// </summary>
/// <returns>Returns the file; access via http://localhost:8081.</returns>
app.MapGet("/", async () =>
{
  Console.WriteLine("Generating video.");

  var path = await GenerateVideoAsync("https://turas.app/s/taiwan/0vylwa7K");

  var stream = File.Open(path, FileMode.Open);

  return Results.File(
    stream,
    contentType: "video/mp4",
    fileDownloadName: path,
    enableRangeProcessing: true
  );
});

app.Run();

/// <summary>
/// Generates a video recording of the given URL and returns the path to the .mp4
/// </summary>
/// <param name="url">The URL of the page to process</param>
/// <returns>The relative path to the output .mp4 file.</returns>
async Task<string> GenerateVideoAsync(string url)
{
  using var playwright = await Playwright.CreateAsync();

  await using var browser = await playwright.Chromium.LaunchAsync(new()
  {
    Headless = true
  });

  await using var context = await browser.NewContextAsync(new()
  {
    ViewportSize = new()
    {
      Width = 430,
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

  await page.GotoAsync(url);

  var handle = await page.WaitForSelectorAsync("span#video", new()
  {
    State = WaitForSelectorState.Attached,
    Timeout = 5000
  });

  // Scroll the page.
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

  // Now we convert it to MP4
  var webmPath = await page.Video!.PathAsync();

  using var stream = File.Open(webmPath, FileMode.Open);

  await FFMpegArguments
    .FromPipeInput(new StreamPipeSource(stream))
    .OutputToFile($"{webmPath}.mp4", false, options => options
      .WithVideoCodec(VideoCodec.LibX264)
    )
    .ProcessAsynchronously();

  return $"{webmPath}.mp4";
}