using Android.Gms.Extensions;
using SkiaSharp;
using Xamarin.Google.MLKit.Common.Model;
using Google.MLKit.Vision.Digitalink.Common;
using Google.MLKit.Vision.Digitalink.Recognition;

namespace Icono.Mobile.Android;

/// <summary>Handwriting recognition with Google ML Kit (Digital Ink): the recognizing is done on the tablet. The model of a language (about 20 MB) is
/// loaded once from Google - that is the only time the app uses the internet.</summary>
internal static class InkRecognizer
{
    public static async Task<HandwritingResult> RecognizeAsync(IReadOnlyList<IReadOnlyList<SKPoint>> strokes, string languageTag, Action<string> status)
    {
        try
        {
            var identifier = DigitalInkRecognitionModelIdentifier.FromLanguageTag(languageTag);
            if (identifier is null)
            {
                return new HandwritingResult(null, "This language cannot be recognized.");
            }

            var model = DigitalInkRecognitionModel.InvokeBuilder(identifier).Build();
            var manager = RemoteModelManager.Instance;
            var present = (Java.Lang.Boolean?)await manager.IsModelDownloaded(model).AsAsync<Java.Lang.Boolean>();
            if (present is null || !present.BooleanValue())
            {
                status("Loading the handwriting model (once, about 20 MB) ...");
                try
                {
                    await manager.Download(model, new DownloadConditions.Builder().Build()).AsAsync<Java.Lang.Void>();
                }
                catch (Java.Lang.Exception)
                {
                    return new HandwritingResult(null, "The handwriting model could not be loaded - it needs the internet once. Try again with a connection.");
                }
            }

            status("Reading the handwriting ...");
            var ink = Ink.InvokeBuilder();
            long time = 0;
            foreach (var stroke in strokes)
            {
                var builder = Ink.Stroke.InvokeBuilder();
                foreach (var point in stroke)
                {
                    builder.AddPoint(Ink.Point.Create(point.X, point.Y, time));
                    time += 8;
                }

                time += 250; // a pause between two strokes
                ink.AddStroke(builder.Build());
            }

            using var recognizer = DigitalInkRecognition.GetClient(DigitalInkRecognizerOptions.InvokeBuilder(model).Build());
            var result = (RecognitionResult?)await recognizer.Recognize(ink.Build()).AsAsync<RecognitionResult>();
            var text = result?.Candidates.FirstOrDefault()?.Text;
            return string.IsNullOrWhiteSpace(text)
                ? new HandwritingResult(null, "No text was recognized - write a little larger and clearer.")
                : new HandwritingResult(text, null);
        }
        catch (Java.Lang.Exception ex)
        {
            return new HandwritingResult(null, "Handwriting could not be recognized: " + ex.Message);
        }
    }
}
