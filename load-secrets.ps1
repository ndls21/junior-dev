# Load Junior Dev Secrets
# Run this script to load secrets from .env.local into your environment
# Usage: .\load-secrets.ps1

$envFile = ".env.local"

if (Test-Path $envFile) {
    Write-Host "Loading secrets from $envFile..."

    Get-Content $envFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and !$line.StartsWith("#")) {
            $key, $value = $line -split "=", 2
            if ($key -and $value) {
                $key = $key.Trim()
                $value = $value.Trim()
                [Environment]::SetEnvironmentVariable($key, $value, "Process")
                Write-Host "Set $key"
            }
        }
    }

    Write-Host "Secrets loaded successfully!"
    Write-Host "You can now run AI tests with: dotnet test --filter 'Category=AI'"
} else {
    Write-Host "Error: $envFile not found. Please create it with your secrets."
    Write-Host "Example content:"
    Write-Host "JUNIORDEV__Auth__OpenAI__ApiKey=your_openai_key_here"
    Write-Host "RUN_AI_TESTS=1"
}