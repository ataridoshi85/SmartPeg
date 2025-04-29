using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Google.Cloud.Language.V1;
using Google.Cloud.AIPlatform.V1;
using Google.Apis.Auth.OAuth2;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Add MVC services
builder.Services.AddControllersWithViews();
builder.Services.AddSession();

builder.Services.AddHttpClient();

builder.Services.AddSingleton<PredictionServiceClient>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var credentialsPath = config["GoogleCloud:CredentialsPath"];

    // Ensure the credentials path is absolute
    if (!Path.IsPathRooted(credentialsPath))
    {
        credentialsPath = Path.Combine(Directory.GetCurrentDirectory(), credentialsPath);
    }

    // Verify credentials file exists
    if (!File.Exists(credentialsPath))
    {
        throw new FileNotFoundException($"Google Cloud credentials file not found at: {credentialsPath}");
    }

    var credential = GoogleCredential.FromFile(credentialsPath)
        .CreateScoped(PredictionServiceClient.DefaultScopes);

    var clientBuilder = new PredictionServiceClientBuilder
    {
        Credential = credential
    };

    return clientBuilder.Build();
});

// Add Google Cloud Language client
builder.Services.AddSingleton<LanguageServiceClient>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var credentialsPath = config["GoogleCloud:CredentialsPath"];

    // Ensure the credentials path is absolute
    if (!Path.IsPathRooted(credentialsPath))
    {
        credentialsPath = Path.Combine(Directory.GetCurrentDirectory(), credentialsPath);
    }

    // Verify credentials file exists
    if (!File.Exists(credentialsPath))
    {
        throw new FileNotFoundException($"Google Cloud credentials file not found at: {credentialsPath}");
    }

    var credential = GoogleCredential.FromFile(credentialsPath)
        .CreateScoped(LanguageServiceClient.DefaultScopes);

    var clientBuilder = new LanguageServiceClientBuilder
    {
        Credential = credential
    };

    return clientBuilder.Build();
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseSession();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


app.Run();

// added spin wheels on homepage and result for the loading, used.
// add try catches, check where it fails with long sample files after loading
// add debug mode
// add a feedback level selector
// add a prompt to the user to analyze the review by category, interface with a result in html not markdown