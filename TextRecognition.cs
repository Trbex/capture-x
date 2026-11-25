using Tesseract;
using Windows.ApplicationModel.DataTransfer;

namespace RealCaptureX;

internal static class TextRecognition
{
    public static async Task<string> CopyAsync(byte[] png)
    {
        var recognized = await Task.Run(() =>
        {
            var models = Path.Combine(AppContext.BaseDirectory, "Assets", "tessdata");
            using var engine = new TesseractEngine(models, "eng+ces", EngineMode.LstmOnly);
            using var image = Pix.LoadFromMemory(png);
            using var page = engine.Process(image, PageSegMode.Auto);
            return page.GetText().Trim();
        });
        if (recognized.Length == 0) return recognized;
        var data = new DataPackage();
        data.SetText(recognized);
        Clipboard.SetContent(data);
        Clipboard.Flush();
        return recognized;
    }
}
