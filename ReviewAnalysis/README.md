# REVIEW ANALYSIS PLATFORM


## Overview

Review Analysis Platform is a comprehensive tool for processing, analyzing, and visualizing employee feedback data. It leverages AI technologies including Google's Natural Language API and Gemini AI to provide sentiment analysis, identify key trends, and generate actionable insights from employee reviews.

### Features
•	Excel File Processing: Upload Excel files containing employee feedback data
•	Sentiment Analysis: Automatic analysis of feedback sentiment using Google's Natural Language API
•	Interactive Visualizations: Dynamic radar charts to visualize sentiment across departments
•	Filtering Capabilities: Filter results by department, seniority, and age demographics
•	AI-Powered Insights: Ask questions about your dataset and receive AI-generated analysis
•	Dual AI Processing: Options for both server-side and client-side AI processing

### Technologies
•	Backend: ASP.NET Core 8.0, C# 12.0
•	Frontend: HTML, CSS, JavaScript, Bootstrap
•	Visualization: ApexCharts
•	AI Services:
•	Google Cloud Natural Language API (sentiment analysis)
•	Google Gemini AI (advanced text generation)
•	Data Processing: NPOI (Excel file handling)


## Getting Started

### Prerequisites
•	.NET 8.0 SDK
•	Google Cloud account with Natural Language API enabled
•	Google AI API key for Gemini

### Configuration
1.	Clone the repository
2.	Add your Google Cloud credentials in appsettings.json:
   "GoogleCloud": {
     "ProjectId": "your-project-id",
     "Location": "your-region",
     "CredentialsPath": "path/to/credentials.json"
   },
   "GoogleAI": {
     "GeminiApiKey": "your-gemini-api-key"
   }
3.	Run dotnet restore to restore dependencies
4.	Run dotnet build to build the project

### Running the Application
1.	Execute dotnet run in the project directory
2.	Navigate to https://localhost:5001 in your browser


## Usage Guide

### Uploading Reviews
1.	From the home page, click "Upload Excel File"
2.	Select your Excel file containing employee reviews
3.	Wait for the processing to complete

### Viewing Analysis Results
•	The system will display a radar chart showing sentiment by department
•	A summary of key insights is shown on the left panel
•	Use filters to narrow down results by department, experience level, or age group

### AI Assistance
1.	Enter a question in the "Ask AI About Your Dataset" section
2.	Click "Submit Question"
3.	View the AI-generated analysis in the response section

### Switching AI Modes
•	Toggle between server-side and client-side AI processing using the "Using Server API" button
•	Server-side uses the API key configured in appsettings.json
•	Client-side requires entering your own API key in the browser


## Excel File Format

### The application expects Excel files with the following columns:
1.	Token/ID (unique identifier)
2.	Age Group (e.g., "Fino a 35", "Oltre 35")
3.	Work Experience (e.g., "Fino a 10", "Oltre 10")
4.	Department (e.g., "IT", "HR", "Finance")
5.	Review text (the actual feedback)

### Privacy and Security
•	All processing is done locally or through secure API calls
•	No data is stored permanently on external servers
•	API keys are handled securely

## Troubleshooting
•	If sentiment analysis fails, check your Google Cloud credentials
•	For Gemini AI issues, verify your API key is valid
•	For Excel processing errors, ensure your file matches the expected format
•	If charts don't render, check your browser console for JavaScript errors


## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments
•	Google Cloud for AI services
•	ApexCharts for visualization components
•	NPOI for Excel file processing
