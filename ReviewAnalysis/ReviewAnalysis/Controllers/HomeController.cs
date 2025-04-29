using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Cloud.Language.V1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using NPOI.XSSF.UserModel;
using System.Text.Json.Serialization;

public class HomeController : Controller
{
    private readonly LanguageServiceClient _languageClient;
    private readonly ILogger<HomeController> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly string _projectId;
    private readonly string _location;
    private readonly string _geminiApiKey;

    public HomeController(LanguageServiceClient languageClient, ILogger<HomeController> logger,
                         IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _languageClient = languageClient;
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient(); // Initialize HttpClient

        // Get Google Cloud config
        _projectId = _configuration["GoogleCloud:ProjectId"];
        _location = _configuration["GoogleCloud:Location"] ?? "europe-west1";

        // Get Gemini API key
        _geminiApiKey = _configuration["GoogleAI:GeminiApiKey"];
        if (string.IsNullOrEmpty(_geminiApiKey))
        {
            _logger.LogWarning("Gemini API key is not configured in appsettings.json");
        }
    }

    public IActionResult Index()
    {
        return View();
    }

    private async Task<string> GenerateDeduction(Dictionary<string, List<double>> results, List<ReviewData> reviews)
    {
        // Use Gemini API for analysis instead of creating it ourselves
        var sb = new StringBuilder();
        sb.AppendLine("<h6 class='mt-4 mb-3'>Analisi dell'AI:</h6>");

        try
        {
            // Overall sentiment analysis
            var overallAverage = results.Count > 0
                ? results.Values.SelectMany(values => values).Average()
                : 0;

            // Create a prompt for Gemini about the overall analysis
            var allReviewTexts = reviews.Select(r => r.Text).ToList();
            var overallAnalysis = await GetGeminiAnalysis(
                $"Analyze the following employee reviews. The average sentiment score is {overallAverage:P0}. " +
                "Provide a concise summary in Italian highlighting key patterns, sentiment, and actionable recommendations. Reviews: " +
                string.Join(" ", allReviewTexts.Take(10)) // Limit to avoid token limits
            );

            sb.AppendLine("<p class='mb-3'><strong>Sentiment Complessivo:</strong> ");
            sb.AppendLine(overallAnalysis);
            sb.AppendLine("</p>");

            // Per category analysis
            sb.AppendLine("<p><strong>Analisi per Categoria:</strong></p><ul class='mb-3'>");
            foreach (var area in results.OrderByDescending(r => r.Value.Average()))
            {
                var areaReviews = reviews.Where(r => r.AreaAziendale == area.Key).ToList();
                var avgScore = area.Value.Average();

                // Demographics information
                var over35Count = areaReviews.Count(r => r.EtaAnagrafica?.Contains("Oltre 35") ?? false);
                var under35Count = areaReviews.Count(r => r.EtaAnagrafica?.Contains("Fino a 35") ?? false);
                var seniorExpCount = areaReviews.Count(r => r.AnzianitaLavorativa?.Contains("Oltre 10") ?? false);
                var juniorExpCount = areaReviews.Count(r => r.AnzianitaLavorativa?.Contains("Fino a 10") ?? false);

                // Create area-specific prompt for Gemini
                var areaTexts = areaReviews.Select(r => r.Text).ToList();
                var areaAnalysis = await GetGeminiAnalysis(
                    $"Analyze these employee reviews for the '{area.Key}' area. The sentiment score is {avgScore:P0}. " +
                    $"Demographics: {over35Count} are over 35 years old, {under35Count} are under 35. " +
                    $"{seniorExpCount} have over 10 years experience, {juniorExpCount} have less than 10 years. " +
                    "Provide a concise analysis in Italian with actionable insights. Reviews: " +
                    string.Join(" ", areaTexts.Take(5)) // Limit to avoid token limits
                );

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

    private async Task<string> GetGeminiAnalysis(string prompt)
    {
        try
        {
            if (string.IsNullOrEmpty(_geminiApiKey))
            {
                return "API key non configurata. Impossibile generare analisi.";
            }

            var geminiEndpoint = $"https://generativelanguage.googleapis.com/v1/models/gemini-1.5-pro-002:generateContent?key={_geminiApiKey}";

            var requestData = new
            {
                contents = new[]
                {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
                generationConfig = new
                {
                    temperature = 0.4,
                    maxOutputTokens = 500
                }
            };

            var jsonContent = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(requestData),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(geminiEndpoint, jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Gemini API error: {StatusCode}, {Error}",
                    response.StatusCode, errorContent);
                return "Errore durante l'analisi.";
            }

            var responseContent = await response.Content.ReadAsStringAsync();

            // Use JsonSerializerOptions to handle property name casing
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var responseObject = JsonSerializer.Deserialize<GeminiResponse>(responseContent, options);

            var analysis = responseObject?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

            return string.IsNullOrEmpty(analysis) ? "Nessuna analisi disponibile." : analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Gemini analysis: {Error}", ex.Message);
            return "Errore durante l'elaborazione dell'analisi.";
        }
    }

    [HttpPost]
    public async Task<IActionResult> AskQuestion([FromBody] QuestionRequest request)
    {
        _logger.LogInformation("AskQuestion method called");

        if (request == null || string.IsNullOrWhiteSpace(request.Question))
        {
            _logger.LogWarning("Empty question received");
            return BadRequest("The question cannot be empty.");
        }

        try
        {
            _logger.LogInformation("Processing question: {Question}", request.Question);

            if (string.IsNullOrEmpty(_geminiApiKey))
            {
                _logger.LogError("Gemini API key is missing");
                return StatusCode(500, "API configuration error");
            }

            var geminiEndpoint = $"https://generativelanguage.googleapis.com/v1/models/gemini-1.5-pro-002:generateContent?key={_geminiApiKey}";

            var requestData = new
            {
                contents = new[]
                {
                new
                {
                    parts = new[]
                    {
                        new { text = request.Question }
                    }
                }
            },
                generationConfig = new
                {
                    temperature = 0.7,
                    topK = 40,
                    topP = 0.95,
                    maxOutputTokens = 800
                }
            };

            var jsonRequest = JsonSerializer.Serialize(requestData);
            _logger.LogInformation("Request JSON: {RequestJson}", jsonRequest);

            var jsonContent = new StringContent(
                jsonRequest,
                Encoding.UTF8,
                "application/json");

            _logger.LogInformation("Sending request to Gemini API");
            var response = await _httpClient.PostAsync(geminiEndpoint, jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Gemini API error: {StatusCode}, {Error}",
                    response.StatusCode, errorContent);
                return StatusCode((int)response.StatusCode, $"Error from AI service: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogInformation("Gemini API response received: {Response}", responseContent);

            // Use JsonSerializerOptions to handle property name casing
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var responseObject = JsonSerializer.Deserialize<GeminiResponse>(responseContent, options);

            if (responseObject == null)
            {
                _logger.LogError("Failed to deserialize the response");
                return Json(new { error = "Failed to process the AI response." });
            }

            _logger.LogInformation("Deserialized response: Candidates count: {CandidatesCount}",
                responseObject.Candidates?.Count ?? 0);

            var answer = responseObject?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            _logger.LogInformation("Extracted answer: {Answer}", answer);

            if (string.IsNullOrEmpty(answer))
            {
                _logger.LogWarning("No answer found in the response");
                return Json(new { error = "No answer could be generated." });
            }

            var result = new { answer };
            _logger.LogInformation("Returning answer: {Result}", JsonSerializer.Serialize(result));
            return Json(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing the question: {Question}", request.Question);
            return StatusCode(500, "An error occurred while processing your request.");
        }
    }

    public class QuestionRequest
    {
        public string Question { get; set; }
    }

    public class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<Candidate> Candidates { get; set; }

        [JsonPropertyName("usageMetadata")]
        public UsageMetadata UsageMetadata { get; set; }

        [JsonPropertyName("modelVersion")]
        public string ModelVersion { get; set; }
    }

    public class Candidate
    {
        [JsonPropertyName("content")]
        public Content Content { get; set; }

        [JsonPropertyName("finishReason")]
        public string FinishReason { get; set; }

        [JsonPropertyName("avgLogprobs")]
        public double AvgLogprobs { get; set; }
    }

    public class Content
    {
        [JsonPropertyName("parts")]
        public List<Part> Parts { get; set; }

        [JsonPropertyName("role")]
        public string Role { get; set; }
    }

    public class Part
    {
        [JsonPropertyName("text")]
        public string Text { get; set; }
    }

    public class UsageMetadata
    {
        [JsonPropertyName("promptTokenCount")]
        public int PromptTokenCount { get; set; }

        [JsonPropertyName("candidatesTokenCount")]
        public int CandidatesTokenCount { get; set; }

        [JsonPropertyName("totalTokenCount")]
        public int TotalTokenCount { get; set; }
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
        ViewBag.Categories = JsonSerializer.Serialize(
            results.Select(r => r.Key).ToList());
        ViewBag.Deductions = await GenerateDeduction(results, reviews);

        // Add filter options to ViewBag
        ViewBag.Areas = reviews.Select(r => r.AreaAziendale).Distinct().OrderBy(x => x).ToList();
        ViewBag.Anzianita = reviews.Select(r => r.AnzianitaLavorativa).Distinct().OrderBy(x => x).ToList();
        ViewBag.Eta = reviews.Select(r => r.EtaAnagrafica).Distinct().OrderBy(x => x).ToList();

        TempData["Reviews"] = JsonSerializer.Serialize(reviews.Select(r => new ReviewData
        {
            Category = JsonSerializer.Serialize((r.Text != null ? results[r.AreaAziendale].Last() : 0)),
            Text = r.Text, // Include the actual review text
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

    public class AreaGroup
    {
        public string Area { get; set; }
        public int Count { get; set; }
        public double AvgSentiment { get; set; }
        public int SeniorCount { get; set; }
        public int JuniorCount { get; set; }
        public int Over35Count { get; set; }
        public int Under35Count { get; set; }
    }

    public class DocumentQuestionRequest
    {
        public string Question { get; set; }
        public string Area { get; set; }
        public string Anzianita { get; set; }
        public string Eta { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> AskDocumentQuestion([FromBody] DocumentQuestionRequest request)
    {
        _logger.LogInformation("AskDocumentQuestion method called with question: {Question}", request.Question);

        if (request == null || string.IsNullOrWhiteSpace(request.Question))
        {
            _logger.LogWarning("Empty question received");
            return BadRequest("The question cannot be empty.");
        }

        if (TempData["Reviews"] == null)
        {
            _logger.LogWarning("No reviews data found in TempData");
            return Json(new { error = "No document data available. Please upload a document first." });
        }

        try
        {
            // Get the reviews from TempData
            var reviews = JsonSerializer.Deserialize<List<ReviewData>>(TempData["Reviews"].ToString());
            TempData.Keep("Reviews"); // Keep the data for subsequent requests

            // Apply filters if provided
            var filteredReviews = reviews.AsQueryable();
            if (!string.IsNullOrEmpty(request.Area))
            {
                filteredReviews = filteredReviews.Where(r => r.AreaAziendale == request.Area);
            }
            if (!string.IsNullOrEmpty(request.Anzianita))
            {
                filteredReviews = filteredReviews.Where(r => r.AnzianitaLavorativa == request.Anzianita);
            }
            if (!string.IsNullOrEmpty(request.Eta))
            {
                filteredReviews = filteredReviews.Where(r => r.EtaAnagrafica == request.Eta);
            }

            var finalFilteredReviews = filteredReviews.ToList();

            if (!finalFilteredReviews.Any())
            {
                return Json(new { answer = "There are no reviews matching the selected filters. Please try different filters." });
            }

            // Prepare context about the reviews for the AI
            var context = new StringBuilder();

            // Add general statistics
            context.AppendLine($"Document Context: Analysis of {finalFilteredReviews.Count()} employee reviews.");

            // Add details about departments
            var departments = finalFilteredReviews.Select(r => r.AreaAziendale).Distinct().ToList();
            context.AppendLine($"Departments covered: {string.Join(", ", departments)}.");

            // Add age and experience breakdowns
            var over35Count = finalFilteredReviews.Count(r => r.EtaAnagrafica?.Contains("Oltre 35") ?? false);
            var under35Count = finalFilteredReviews.Count(r => r.EtaAnagrafica?.Contains("Fino a 35") ?? false);
            context.AppendLine($"Demographics: {over35Count} employees over 35 years old, {under35Count} under 35 years old.");

            var seniorCount = finalFilteredReviews.Count(r => r.AnzianitaLavorativa?.Contains("Oltre 10") ?? false);
            var juniorCount = finalFilteredReviews.Count(r => r.AnzianitaLavorativa?.Contains("Fino a 10") ?? false);
            context.AppendLine($"Experience levels: {seniorCount} with over 10 years experience, {juniorCount} with under 10 years.");

            // Include the actual review texts (limit to avoid token limits)
            context.AppendLine("\nReview samples:");
            foreach (var department in departments)
            {
                var deptReviews = finalFilteredReviews.Where(r => r.AreaAziendale == department).Take(3);
                foreach (var review in deptReviews)
                {
                    context.AppendLine($"- Department: {review.AreaAziendale}, Age: {review.EtaAnagrafica}, Experience: {review.AnzianitaLavorativa}");
                    context.AppendLine($"  Review: {review.Text.Substring(0, Math.Min(300, review.Text.Length))}...\n");
                }
            }

            // Create the prompt for Gemini
            var prompt = $"Sei un analista esperto di recensioni dipendenti. Basandoti sul seguente dataset di recensioni dipendenti, per favore rispondi a questa domanda: \"{request.Question}\"\n\n{context}";

            _logger.LogInformation("Sending question to Gemini API with context about {ReviewCount} reviews", finalFilteredReviews.Count());

            if (string.IsNullOrEmpty(_geminiApiKey))
            {
                _logger.LogError("Gemini API key is missing");
                return StatusCode(500, "API configuration error");
            }

            var geminiEndpoint = $"https://generativelanguage.googleapis.com/v1/models/gemini-1.5-pro-002:generateContent?key={_geminiApiKey}";

            var requestData = new
            {
                contents = new[]
                {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
                generationConfig = new
                {
                    temperature = 0.4,
                    topK = 40,
                    topP = 0.95,
                    maxOutputTokens = 1000
                }
            };

            var jsonRequest = JsonSerializer.Serialize(requestData);
            _logger.LogDebug("Request JSON: {RequestJson}", jsonRequest);

            var jsonContent = new StringContent(
                jsonRequest,
                Encoding.UTF8,
                "application/json");

            _logger.LogInformation("Sending request to Gemini API");
            var response = await _httpClient.PostAsync(geminiEndpoint, jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Gemini API error: {StatusCode}, {Error}",
                    response.StatusCode, errorContent);
                return StatusCode((int)response.StatusCode, $"Error from AI service: {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Gemini API response received: {Response}", responseContent);

            // Use JsonSerializerOptions to handle property name casing
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var responseObject = JsonSerializer.Deserialize<GeminiResponse>(responseContent, options);

            if (responseObject == null)
            {
                _logger.LogError("Failed to deserialize the response");
                return Json(new { error = "Failed to process the AI response." });
            }

            var answer = responseObject?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
            _logger.LogInformation("Generated answer for document question");

            if (string.IsNullOrEmpty(answer))
            {
                _logger.LogWarning("No answer found in the response");
                return Json(new { error = "No answer could be generated." });
            }

            return Json(new { answer });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing document question: {Question}", request.Question);
            return StatusCode(500, "An error occurred while processing your request.");
        }
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
