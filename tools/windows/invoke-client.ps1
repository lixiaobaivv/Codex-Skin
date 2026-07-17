function Invoke-CodexSkinClient {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Client,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory = (Get-Location).Path
    )

    $id = [Guid]::NewGuid().ToString('N')
    $stdout = Join-Path $env:RUNNER_TEMP "codex-skin-$id.stdout"
    $stderr = Join-Path $env:RUNNER_TEMP "codex-skin-$id.stderr"
    try {
        $process = Start-Process `
            -FilePath $Client `
            -ArgumentList $Arguments `
            -WorkingDirectory $WorkingDirectory `
            -NoNewWindow `
            -Wait `
            -PassThru `
            -RedirectStandardOutput $stdout `
            -RedirectStandardError $stderr
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = @(Get-Content -LiteralPath $stdout -ErrorAction SilentlyContinue)
            Error = @(Get-Content -LiteralPath $stderr -ErrorAction SilentlyContinue)
        }
    }
    finally {
        Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue
    }
}
