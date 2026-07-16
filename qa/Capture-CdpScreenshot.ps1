param(
    [Parameter(Mandatory = $true)]
    [string]$OutFile,
    [int]$Width = 1586,
    [int]$Height = 992,
    [string]$BeforeCapture = ""
)

$ErrorActionPreference = "Stop"
$targets = Invoke-RestMethod "http://127.0.0.1:9229/json/list"
$target = $targets |
    Where-Object {
        $_.type -eq "page" -and
        ($_.url -match "codex" -or $_.title -match "codex") -and
        $_.webSocketDebuggerUrl -match "^ws://(127\.0\.0\.1|localhost):9229/"
    } |
    Select-Object -First 1

if (-not $target) {
    throw "No Codex page target is available on loopback CDP port 9229."
}

$socket = [System.Net.WebSockets.ClientWebSocket]::new()
$socket.ConnectAsync(
    [Uri]$target.webSocketDebuggerUrl,
    [Threading.CancellationToken]::None
).GetAwaiter().GetResult() | Out-Null

$script:commandId = 0
$script:cdpErrors = [Collections.Generic.List[string]]::new()
function Invoke-CdpCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [hashtable]$Params = @{}
    )

    $script:commandId += 1
    $id = $script:commandId
    $json = @{ id = $id; method = $Method; params = $Params } |
        ConvertTo-Json -Compress -Depth 12
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $segment = [ArraySegment[byte]]::new($bytes)
    $socket.SendAsync(
        $segment,
        [System.Net.WebSockets.WebSocketMessageType]::Text,
        $true,
        [Threading.CancellationToken]::None
    ).GetAwaiter().GetResult()

    $buffer = [byte[]]::new(65536)
    while ($true) {
        $stream = [IO.MemoryStream]::new()
        try {
            do {
                $receiveSegment = [ArraySegment[byte]]::new($buffer)
                $result = $socket.ReceiveAsync(
                    $receiveSegment,
                    [Threading.CancellationToken]::None
                ).GetAwaiter().GetResult()
                if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
                    throw "CDP WebSocket closed before command $Method completed."
                }
                $stream.Write($buffer, 0, $result.Count)
            } while (-not $result.EndOfMessage)

            $message = [Text.Encoding]::UTF8.GetString($stream.ToArray()) |
                ConvertFrom-Json -AsHashtable
            if ($message.id -ne $id) {
                if ($message.method -eq "Runtime.exceptionThrown") {
                    $description = $message.params.exceptionDetails.exception.description
                    if (-not $description) { $description = $message.params.exceptionDetails.text }
                    $script:cdpErrors.Add([string]$description)
                }
                elseif ($message.method -eq "Log.entryAdded" -and $message.params.entry.level -eq "error") {
                    $script:cdpErrors.Add([string]$message.params.entry.text)
                }
                continue
            }
            if ($message.error) {
                throw "CDP $Method failed: $($message.error.message)"
            }
            return $message.result
        }
        finally {
            $stream.Dispose()
        }
    }
}

try {
    Invoke-CdpCommand "Page.enable" | Out-Null
    Invoke-CdpCommand "Runtime.enable" | Out-Null
    Invoke-CdpCommand "Log.enable" | Out-Null
    Invoke-CdpCommand "Emulation.setDeviceMetricsOverride" @{
        width = $Width
        height = $Height
        deviceScaleFactor = 1
        mobile = $false
    } | Out-Null

    if (-not [string]::IsNullOrWhiteSpace($BeforeCapture)) {
        $beforeResult = Invoke-CdpCommand "Runtime.evaluate" @{
            expression = $BeforeCapture
            awaitPromise = $true
            returnByValue = $true
        }
        if ($beforeResult.exceptionDetails) {
            throw "BeforeCapture JavaScript raised an exception."
        }
    }

    Invoke-CdpCommand "Runtime.evaluate" @{
        expression = "document.fonts.ready.then(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))))"
        awaitPromise = $true
        returnByValue = $true
    } | Out-Null

    $capture = Invoke-CdpCommand "Page.captureScreenshot" @{
        format = "png"
        fromSurface = $true
        captureBeyondViewport = $false
    }
    $absoluteOut = [IO.Path]::GetFullPath($OutFile)
    $parent = [IO.Path]::GetDirectoryName($absoluteOut)
    if ($parent) {
        [IO.Directory]::CreateDirectory($parent) | Out-Null
    }
    [IO.File]::WriteAllBytes($absoluteOut, [Convert]::FromBase64String($capture.data))

    $state = Invoke-CdpCommand "Runtime.evaluate" @{
        expression = "(() => { const home = document.getElementById('codex-theme-home'); const hero = document.querySelector('.codex-theme-home-hero'); const editor = document.querySelector('.ProseMirror'); const composer = document.querySelector('.composer-surface-chrome'); const logo = document.querySelector('.codex-theme-sidebar-logo-image'); const heroImage = hero ? getComputedStyle(hero).backgroundImage : ''; const imageVariable = getComputedStyle(document.documentElement).getPropertyValue('--codex-theme-background-image'); const homeRect = home?.getBoundingClientRect(); const composerRect = composer?.getBoundingClientRect(); const visibleCards = [...document.querySelectorAll('.codex-theme-home-action')].filter(card => { const rect = card.getBoundingClientRect(); return !!homeRect && rect.bottom > homeRect.top && rect.top < homeRect.bottom; }); const cardComposerOverlap = !!homeRect && !!composerRect && visibleCards.some(card => { const rect = card.getBoundingClientRect(); const visibleBottom = Math.min(rect.bottom, homeRect.bottom); const visibleTop = Math.max(rect.top, homeRect.top); return visibleBottom > composerRect.top && visibleTop < composerRect.bottom && rect.right > composerRect.left && rect.left < composerRect.right; }); return { themeId: document.documentElement.dataset.codexThemeId || '', homeVisible: home ? getComputedStyle(home).display !== 'none' : false, sidebar: !!document.getElementById('codex-theme-sidebar'), style: !!document.getElementById('codex-theme-store-live-style'), viewport: [innerWidth, innerHeight], editorText: (editor?.textContent || '').trim(), horizontalOverflow: document.documentElement.scrollWidth > innerWidth || document.body.scrollWidth > innerWidth, homeScroll: home ? [home.clientHeight, home.scrollHeight] : [0, 0], homeBottom: homeRect ? Math.round(homeRect.bottom) : 0, composerTop: composerRect ? Math.round(composerRect.top) : 0, cardComposerOverlap, logoLoaded: !!logo && logo.complete && logo.naturalWidth > 0, logoNaturalSize: logo ? [logo.naturalWidth, logo.naturalHeight] : [0, 0], logoSourceLength: logo?.currentSrc?.length || 0, heroImageLength: heroImage.length, heroImagePrefix: heroImage.slice(0, 80), imageVariableLength: imageVariable.length, imageVariablePrefix: imageVariable.slice(0, 80) }; })()"
        returnByValue = $true
    }

    [PSCustomObject]@{
        path = $absoluteOut
        bytes = (Get-Item -LiteralPath $absoluteOut).Length
        themeId = $state.result.value.themeId
        homeVisible = $state.result.value.homeVisible
        sidebar = $state.result.value.sidebar
        style = $state.result.value.style
        viewport = ($state.result.value.viewport -join "x")
        editorText = $state.result.value.editorText
        horizontalOverflow = $state.result.value.horizontalOverflow
        homeScroll = ($state.result.value.homeScroll -join "/")
        homeBottom = $state.result.value.homeBottom
        composerTop = $state.result.value.composerTop
        cardComposerOverlap = $state.result.value.cardComposerOverlap
        logoLoaded = $state.result.value.logoLoaded
        logoNaturalSize = ($state.result.value.logoNaturalSize -join "x")
        logoSourceLength = $state.result.value.logoSourceLength
        heroImageLength = $state.result.value.heroImageLength
        heroImagePrefix = $state.result.value.heroImagePrefix
        imageVariableLength = $state.result.value.imageVariableLength
        imageVariablePrefix = $state.result.value.imageVariablePrefix
        consoleErrorCount = $script:cdpErrors.Count
        consoleErrors = @($script:cdpErrors)
    } | ConvertTo-Json -Compress
}
finally {
    if ($socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
        $socket.CloseAsync(
            [System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure,
            "capture complete",
            [Threading.CancellationToken]::None
        ).GetAwaiter().GetResult() | Out-Null
    }
    $socket.Dispose()
}
