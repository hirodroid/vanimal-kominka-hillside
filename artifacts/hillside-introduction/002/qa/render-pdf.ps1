param([string]$PdfPath,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null=[Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]
$null=[Windows.Data.Pdf.PdfDocument,Windows.Data.Pdf,ContentType=WindowsRuntime]
$null=[Windows.Storage.Streams.InMemoryRandomAccessStream,Windows.Storage.Streams,ContentType=WindowsRuntime]
$null=[Windows.Storage.Streams.DataReader,Windows.Storage.Streams,ContentType=WindowsRuntime]
$null=[Windows.Data.Pdf.PdfPageRenderOptions,Windows.Data.Pdf,ContentType=WindowsRuntime]
function Await-Result($Operation,[Type]$ResultType){
    $method=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 } | Select-Object -First 1
    $task=$method.MakeGenericMethod($ResultType).Invoke($null,@($Operation));$task.Wait();return $task.Result
}
function Await-Action($Operation){
    $method=[System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and -not $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 } | Select-Object -First 1
    $task=$method.Invoke($null,@($Operation));$task.Wait()
}
$PdfPath=(Resolve-Path -LiteralPath $PdfPath).Path
$file=Await-Result ([Windows.Storage.StorageFile]::GetFileFromPathAsync($PdfPath)) ([Windows.Storage.StorageFile])
$doc=Await-Result ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
if($doc.PageCount -ne 8){throw 'Expected eight PDF pages'}
for($i=0;$i -lt $doc.PageCount;$i++){
    $page=$doc.GetPage($i);$stream=New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
    try{
        $options=New-Object Windows.Data.Pdf.PdfPageRenderOptions;$options.DestinationWidth=1600
        Await-Action ($page.RenderToStreamAsync($stream,$options))
        $reader=New-Object Windows.Storage.Streams.DataReader($stream.GetInputStreamAt(0))
        try{
            $null=Await-Result ($reader.LoadAsync([uint32]$stream.Size)) ([uint32])
            $bytes=New-Object byte[] ([int]$stream.Size);$reader.ReadBytes($bytes)
            [IO.File]::WriteAllBytes((Join-Path $OutputDirectory ('pdf-page-'+($i+1)+'.png')),$bytes)
        }finally{$reader.Dispose()}
    }finally{$stream.Dispose();$page.Dispose()}
}
Write-Output ('Rendered '+$doc.PageCount+' PDF pages')
