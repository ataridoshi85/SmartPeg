using System.Text;
using System.Text.Json;
using Google.Cloud.Language.V1;
using Microsoft.AspNetCore.Mvc;
using NPOI.XSSF.UserModel;

public class HomeController : Controller
{
    private readonly LanguageServiceClient _languageClient;
    private readonly ILogger<HomeController> _logger;

    public HomeController(LanguageServiceClient languageClient, ILogger<HomeController> logger)
    {
        _languageClient = languageClient;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

    private async Task<string> GenerateDeduction(Dictionary<string, List<double>> results, List<ReviewData> reviews)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<h6 class='mt-4 mb-3'>Analisi dell'AI:</h6>");

        try
        {
            // Overall sentiment analysis
            var overallAverage = results.Count > 0
                ? results.Values.SelectMany(values => values).Average()
                : 0;

            var overallAnalysis = await AnalyzeAreaSentiment("Analisi Complessiva",
                reviews.Select(r => r.Text).ToList(), overallAverage);
            sb.AppendLine("<p class='mb-3'><strong>Sentiment Complessivo:</strong> ");
            sb.AppendLine(overallAnalysis);
            sb.AppendLine("</p>");

            // Per category analysis
            sb.AppendLine("<p><strong>Analisi per Categoria:</strong></p><ul class='mb-3'>");
            foreach (var area in results.OrderByDescending(r => r.Value.Average()))
            {
                var areaReviews = reviews.Where(r => r.AreaAziendale == area.Key).ToList();
                var avgScore = area.Value.Average();

                var areaAnalysis = await AnalyzeAreaSentiment(
                    area.Key,
                    areaReviews.Select(r => r.Text).ToList(),
                    avgScore,
                    new
                    {
                        Over35 = areaReviews.Count(r => r.EtaAnagrafica?.Contains("Oltre 35") ?? false),
                        Under35 = areaReviews.Count(r => r.EtaAnagrafica?.Contains("Fino a 35") ?? false),
                        SeniorExp = areaReviews.Count(r => r.AnzianitaLavorativa?.Contains("Oltre 10") ?? false),
                        JuniorExp = areaReviews.Count(r => r.AnzianitaLavorativa?.Contains("Fino a 10") ?? false)
                    });

                sb.AppendLine($"<li><strong>{area.Key}:</strong> {areaAnalysis}</li>");
            }
            sb.AppendLine("</ul>");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating AI deductions");
            sb.AppendLine("<p class='text-danger'>Si è verificato un errore durante l'analisi. Riprova più tardi.</p>");
        }

        return sb.ToString();
    }

    private async Task<string> AnalyzeAreaSentiment(string area, List<string> reviews, double avgScore, dynamic demographics = null)
    {
        try
        {
            // Combine reviews for content analysis
            var combinedText = string.Join(" ", reviews);
            var document = Document.FromPlainText(combinedText);

            // Get detailed sentiment analysis
            var sentiment = await _languageClient.AnalyzeSentimentAsync(document);

            // Get entity analysis
            var entities = await _languageClient.AnalyzeEntitiesAsync(document);

            // Build analysis based on multiple factors
            var analysis = new StringBuilder();

            // Add sentiment analysis
            var sentimentTone = avgScore switch
            {
                >= 0.8 => "molto positivo",
                >= 0.6 => "positivo",
                >= 0.4 => "neutro",
                >= 0.2 => "critico",
                _ => "molto critico"
            };

            // Base analysis on sentiment and magnitude
            analysis.Append($"Il sentiment è {sentimentTone} ({avgScore:P0}) ");

            if (sentiment.DocumentSentiment.Magnitude > 2.0)
            {
                analysis.Append("con forti variazioni nelle opinioni. ");
            }
            else if (sentiment.DocumentSentiment.Magnitude > 1.0)
            {
                analysis.Append("con moderate variazioni nelle opinioni. ");
            }
            else
            {
                analysis.Append("con opinioni generalmente consistenti. ");
            }

            // Add demographic insights if available
            if (demographics != null)
            {
                if (demographics.Over35 > demographics.Under35)
                {
                    analysis.Append($"Il feedback proviene principalmente da personale senior (>35 anni). ");
                }
                if (demographics.SeniorExp > demographics.JuniorExp)
                {
                    analysis.Append($"Prevale l'esperienza consolidata (>10 anni). ");
                }
            }

            // Add actionable insights based on sentiment
            if (avgScore < 0.4)
            {
                analysis.Append("Si suggerisce un'analisi approfondita delle criticità emerse. ");
            }
            else if (avgScore < 0.6)
            {
                analysis.Append("Si raccomandano interventi mirati di miglioramento. ");
            }
            else if (avgScore < 0.8)
            {
                analysis.Append("Si consiglia di mantenere e rafforzare le pratiche positive. ");
            }
            else
            {
                analysis.Append("Si suggerisce di condividere le best practice con altre aree. ");
            }

            return analysis.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing area sentiment for {Area}", area);
            return "Analisi non disponibile.";
        }
    }

    [HttpPost]
    public async Task<IActionResult> AnalyzeReviews(IFormFile excelFile)
    {
        if (excelFile == null) return BadRequest("Nessun file caricato");

        var reviews = new List<ReviewData>();
        using (var stream = excelFile.OpenReadStream())
        {
            var workbook = new XSSFWorkbook(stream);
            var sheet = workbook.GetSheetAt(0);

            for (int row = 1; row <= sheet.LastRowNum; row++)
            {
                var currentRow = sheet.GetRow(row);
                if (currentRow == null) continue;

                var token = currentRow.GetCell(0)?.ToString();
                var eta = currentRow.GetCell(1)?.ToString();
                var anzianita = currentRow.GetCell(2)?.ToString();
                var area = currentRow.GetCell(3)?.ToString();

                var reviewText = new StringBuilder();
                for (int i = 4; i < currentRow.LastCellNum; i++)
                {
                    var cellValue = currentRow.GetCell(i)?.ToString();
                    if (!string.IsNullOrEmpty(cellValue))
                    {
                        reviewText.Append(cellValue + " ");
                    }
                }

                if (!string.IsNullOrEmpty(token))
                {
                    reviews.Add(new ReviewData
                    {
                        Category = token,
                        Text = reviewText.ToString().Trim(),
                        EtaAnagrafica = eta,
                        AnzianitaLavorativa = anzianita,
                        AreaAziendale = area
                    });
                }
            }
        }

        var results = new Dictionary<string, List<double>>();
        foreach (var review in reviews)
        {
            try
            {
                var document = Document.FromPlainText(review.Text);
                var sentiment = await _languageClient.AnalyzeSentimentAsync(document);

                if (!results.ContainsKey(review.AreaAziendale))
                    results[review.AreaAziendale] = new List<double>();

                results[review.AreaAziendale].Add((sentiment.DocumentSentiment.Score + 1) / 2);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing text: {Text}", review.Text);
            }
        }

        ViewBag.ChartData = string.Join(",", results.Select(r => r.Value.Average()));
        ViewBag.Categories = System.Text.Json.JsonSerializer.Serialize(
            results.Select(r => r.Key).ToList());
        ViewBag.Deductions = await GenerateDeduction(results, reviews);

        // Add filter options to ViewBag
        ViewBag.Areas = reviews.Select(r => r.AreaAziendale).Distinct().OrderBy(x => x).ToList();
        ViewBag.Anzianita = reviews.Select(r => r.AnzianitaLavorativa).Distinct().OrderBy(x => x).ToList();
        ViewBag.Eta = reviews.Select(r => r.EtaAnagrafica).Distinct().OrderBy(x => x).ToList();

        TempData["Reviews"] = JsonSerializer.Serialize(reviews.Select(r => new ReviewData
        {
            Category = JsonSerializer.Serialize((r.Text != null ? results[r.AreaAziendale].Last() : 0)),
            AreaAziendale = r.AreaAziendale,
            AnzianitaLavorativa = r.AnzianitaLavorativa,
            EtaAnagrafica = r.EtaAnagrafica
        }));


        return View("Result");
    }

    [HttpGet]
    public async Task<IActionResult> FilterResultsAsync(string area, string anzianita, string eta)
    {
        if (TempData["Reviews"] == null)
        {
            return Json(new { error = "No data available" });
        }

        var reviews = JsonSerializer.Deserialize<List<ReviewData>>(TempData["Reviews"].ToString());
        TempData.Keep("Reviews"); // Keep the data for subsequent requests

        // Apply filters using LINQ
        var filteredReviews = reviews.AsQueryable();
        if (!string.IsNullOrEmpty(area))
        {
            filteredReviews = filteredReviews.Where(r => r.AreaAziendale == area);
        }
        if (!string.IsNullOrEmpty(anzianita))
        {
            filteredReviews = filteredReviews.Where(r => r.AnzianitaLavorativa == anzianita);
        }
        if (!string.IsNullOrEmpty(eta))
        {
            filteredReviews = filteredReviews.Where(r => r.EtaAnagrafica == eta);
        }

        // Calculate results for filtered data
        var results = filteredReviews
            .GroupBy(r => r.AreaAziendale)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => JsonSerializer.Deserialize<double>(r.Category)).ToList()
            );

        var chartData = string.Join(",", results.Select(r => r.Value.Average()));
        var categories = JsonSerializer.Serialize(results.Select(r => r.Key).ToList());

        return Json(new
        {
            chartData = chartData,
            categories = categories,
            deductions = await GenerateDeduction(results, filteredReviews.ToList())
        });
    }
}

public class ReviewData
{
    public string Category { get; set; }
    public string Text { get; set; }
    public string AreaAziendale { get; set; }
    public string AnzianitaLavorativa { get; set; }
    public string EtaAnagrafica { get; set; }
}
