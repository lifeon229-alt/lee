using System.Runtime.InteropServices.WindowsRuntime;
using AbyssBot.Core.Ocr;
using OpenCvSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using WinOcr = Windows.Media.Ocr.OcrEngine;

namespace AbyssBot.App.Platform;

/// <summary>Windows.Media.Ocr 한국어 OCR. Windows 설정에서 한국어 언어 팩(OCR 포함)이 설치돼 있어야 한다.</summary>
public sealed class WinOcrEngine : IOcrEngine
{
    private readonly WinOcr _engine;
    private readonly object _lock = new();

    private WinOcrEngine(WinOcr engine) => _engine = engine;

    public static WinOcrEngine? TryCreate(out string? problem)
    {
        var lang = new Language("ko");
        if (!WinOcr.IsLanguageSupported(lang))
        {
            problem = "Windows 한국어 OCR을 사용할 수 없습니다. 설정 > 시간 및 언어 > 언어에서 한국어(광학 문자 인식 포함)를 설치하세요. " +
                      $"현재 사용 가능: {string.Join(", ", WinOcr.AvailableRecognizerLanguages.Select(l => l.LanguageTag))}";
            return null;
        }
        var e = WinOcr.TryCreateFromLanguage(lang);
        problem = e is null ? "한국어 OCR 엔진 생성 실패" : null;
        return e is null ? null : new WinOcrEngine(e);
    }

    public OcrResult Recognize(Mat bgr)
    {
        if (bgr.Width < 1 || bgr.Height < 1) return OcrResult.Empty;
        using var bgra = new Mat();
        Cv2.CvtColor(bgr, bgra, ColorConversionCodes.BGR2BGRA);
        var bytes = new byte[bgra.Width * bgra.Height * 4];
        // CvtColor 결과는 항상 연속 메모리다.
        System.Runtime.InteropServices.Marshal.Copy(bgra.Data, bytes, 0, bytes.Length);

        using var sb = SoftwareBitmap.CreateCopyFromBuffer(bytes.AsBuffer(), BitmapPixelFormat.Bgra8, bgra.Width, bgra.Height, BitmapAlphaMode.Premultiplied);
        Windows.Media.Ocr.OcrResult r;
        lock (_lock) r = _engine.RecognizeAsync(sb).AsTask().GetAwaiter().GetResult();

        var lines = r.Lines.Select(l => new OcrLine(l.Words.Select(w => new OcrWord(w.Text, new Rect(
            (int)Math.Floor(w.BoundingRect.X), (int)Math.Floor(w.BoundingRect.Y),
            Math.Max(1, (int)Math.Ceiling(w.BoundingRect.Width)), Math.Max(1, (int)Math.Ceiling(w.BoundingRect.Height))))).ToList())).ToList();
        return new OcrResult(lines);
    }
}
