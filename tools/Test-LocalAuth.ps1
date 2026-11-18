param(
    [Uri] $BaseUri = 'http://localhost:8080/',
    [string] $ReportPath
)

$ErrorActionPreference = 'Stop'
if ($BaseUri.Scheme -ne 'https' -and !($BaseUri.Scheme -eq 'http' -and $BaseUri.IsLoopback))
{
    throw 'HTTP is allowed only for a local development service.'
}

# Invalid inputs are intentional. These checks do not create accounts, send email or change passwords.
$checks = @(
    @{ Name = 'liveness'; Method = 'GET'; Path = 'health/live'; Expected = 200 },
    @{ Name = 'readiness'; Method = 'GET'; Path = 'health/ready'; Expected = 200 },
    @{ Name = 'identity_without_token'; Method = 'GET'; Path = 'api/v1/sessions/current'; Expected = 401;
       Code = 'INVALID_ACCESS_TOKEN' },
    @{ Name = 'logout_without_token'; Method = 'DELETE'; Path = 'api/v1/sessions/current'; Expected = 401;
       Code = 'INVALID_ACCESS_TOKEN' },
    @{ Name = 'invalid_login'; Method = 'POST'; Path = 'api/v1/sessions'; Expected = 401;
       Code = 'INVALID_CREDENTIALS';
       Body = @{ email = 'clusterjam-smoke-nobody@example.invalid'; password = 'SyntheticUnusablePassword123!' } },
    @{ Name = 'invalid_recovery'; Method = 'POST'; Path = 'api/v1/password-recoveries'; Expected = 422;
       Code = 'VALIDATION_ERROR';
       Body = @{ email = 'nobody@example.invalid'; code = '12345'; newPassword = 'NewSyntheticPassword123!' } },
    @{ Name = 'invalid_refresh'; Method = 'POST'; Path = 'api/v1/session-renewals'; Expected = 401;
       Code = 'INVALID_REFRESH_TOKEN'; Body = @{ refreshToken = 'invalid-synthetic-refresh-token' } },
    @{ Name = 'invalid_recovery_request'; Method = 'POST'; Path = 'api/v1/password-recovery-requests'; Expected = 422;
       Code = 'VALIDATION_ERROR'; Body = @{ email = 'invalid' } }
)

$results = foreach ($check in $checks)
{
    $arguments = @{
        Uri = [Uri]::new($BaseUri, $check.Path)
        Method = $check.Method
        SkipHttpErrorCheck = $true
        TimeoutSec = 15
    }
    if ($check.ContainsKey('Body'))
    {
        $arguments.Body = $check.Body | ConvertTo-Json -Compress
        $arguments.ContentType = 'application/json'
    }
    $response = Invoke-WebRequest @arguments
    $jsonText = if ($response.Content -is [byte[]])
    {
        [Text.Encoding]::UTF8.GetString($response.Content)
    }
    else { [string]$response.Content }
    $body = $jsonText | ConvertFrom-Json
    $passed = [int]$response.StatusCode -eq $check.Expected
    if ($check.ContainsKey('Code'))
    {
        $mediaType = ($response.Headers['Content-Type'] -join ', ').Split(';')[0]
        $passed = $passed -and $body.code -eq $check.Code -and $body.status -eq $check.Expected `
            -and $mediaType -eq 'application/problem+json' -and $body.traceId -match '^[0-9a-f]{32}$'
        foreach ($required in @('type', 'title', 'status', 'detail', 'instance', 'code'))
        {
            $passed = $passed -and $null -ne $body.$required
        }
    }
    else { $passed = $passed -and $body.status -eq 'ok' }
    [pscustomobject]@{
        Check = $check.Name
        Status = [int]$response.StatusCode
        Code = $body.code
        Passed = [bool]$passed
    }
    Start-Sleep -Milliseconds 250
}

$results | Format-Table -AutoSize
if ($ReportPath)
{
    @{
        checkedAt = [DateTimeOffset]::UtcNow.ToString('o')
        baseUrl = $BaseUri.AbsoluteUri
        checks = @($results)
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding utf8
}
if (@($results | Where-Object { !$_.Passed }).Count -gt 0)
{
    throw 'One or more live Auth checks failed.'
}
