param (
    [string]$TargetDir = "d:\MyProjects\PersonalAIAssistant_Memory"
)

function Convert-File([string]$filePath) {
    $lines = [System.IO.File]::ReadAllLines($filePath)
    
    # Check if already file-scoped
    foreach ($l in $lines) {
        if ($l -match '^\s*namespace\s+[a-zA-Z0-9_.]+\s*;') {
            return $false
        }
    }
    
    $nsLineIdx = -1
    $nsName = $null
    $hasSameLineBrace = $false
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^\s*namespace\s+([a-zA-Z0-9_.]+)\s*(\{)?\s*$') {
            $nsLineIdx = $i
            $nsName = $matches[1]
            if ($matches[2] -eq '{') { $hasSameLineBrace = $true }
            break
        }
    }
    
    if ($nsLineIdx -eq -1) { return $false }
    
    $braceLineIdx = -1
    if ($hasSameLineBrace) {
        $braceLineIdx = $nsLineIdx
    } else {
        for ($i = $nsLineIdx + 1; $i -lt $lines.Length; $i++) {
            if ($lines[$i] -match '^\s*\{\s*$') {
                $braceLineIdx = $i
                break
            }
            if ($lines[$i].Trim() -ne "") {
                return $false
            }
        }
    }
    if ($braceLineIdx -eq -1) { return $false }
    
    # Find outermost closing brace: the last '}' in file
    $lastBraceIdx = -1
    for ($i = $lines.Length - 1; $i -gt $braceLineIdx; $i--) {
        if ($lines[$i].Trim() -eq '}') {
            $lastBraceIdx = $i
            break
        }
    }
    if ($lastBraceIdx -eq -1) { return $false }
    
    $newLines = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $nsLineIdx; $i++) {
        $newLines.Add($lines[$i])
    }
    
    $newLines.Add("namespace $nsName;")
    
    # Check if first inner line is not empty, if so add blank line
    $firstInnerLine = $lines[$braceLineIdx + 1]
    if ($firstInnerLine.Trim() -ne "") {
        $newLines.Add("")
    }
    
    for ($i = $braceLineIdx + 1; $i -lt $lastBraceIdx; $i++) {
        $l = $lines[$i]
        if ($l.StartsWith("    ")) {
            $newLines.Add($l.Substring(4))
        } elseif ($l.StartsWith("`t")) {
            $newLines.Add($l.Substring(1))
        } else {
            $newLines.Add($l)
        }
    }
    
    for ($i = $lastBraceIdx + 1; $i -lt $lines.Length; $i++) {
        $newLines.Add($lines[$i])
    }
    
    $utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllLines($filePath, $newLines, $utf8WithoutBom)
    return $true
}

$csFiles = Get-ChildItem -Path $TargetDir -Recurse -Filter "*.cs" | 
    Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' }

$converted = 0
foreach ($f in $csFiles) {
    if (Convert-File $f.FullName) {
        $converted++
        Write-Host "Converted: $($f.FullName)"
    }
}
Write-Host "Total converted: $converted files."
