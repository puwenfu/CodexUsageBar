function New-VersionedTestExecutable {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ProductVersion,
        [Parameter(Mandatory = $true)][string]$FileVersion
    )

    $namespace = 'PublishFixture_' + [Guid]::NewGuid().ToString('N')
    $source = @"
using System.Reflection;
[assembly: AssemblyVersion("$FileVersion")]
[assembly: AssemblyFileVersion("$FileVersion")]
[assembly: AssemblyInformationalVersion("$ProductVersion")]
namespace $namespace
{
    internal static class Program
    {
        private static void Main() { }
    }
}
"@

    Add-Type `
        -TypeDefinition $source `
        -OutputAssembly $Path `
        -OutputType WindowsApplication
}

function New-VersionlessTestExecutable {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    $bytes = New-Object byte[] 1024

    function Set-UInt16LittleEndian {
        param([int]$Offset, [uint16]$Value)
        [BitConverter]::GetBytes($Value).CopyTo($bytes, $Offset)
    }

    function Set-UInt32LittleEndian {
        param([int]$Offset, [uint32]$Value)
        [BitConverter]::GetBytes($Value).CopyTo($bytes, $Offset)
    }

    $bytes[0] = 0x4D
    $bytes[1] = 0x5A
    Set-UInt32LittleEndian -Offset 0x3C -Value 0x80
    $bytes[0x80] = 0x50
    $bytes[0x81] = 0x45

    Set-UInt16LittleEndian -Offset 0x84 -Value 0x14C
    Set-UInt16LittleEndian -Offset 0x86 -Value 1
    Set-UInt16LittleEndian -Offset 0x94 -Value 0xE0
    Set-UInt16LittleEndian -Offset 0x96 -Value 0x0102

    $optionalHeader = 0x98
    Set-UInt16LittleEndian -Offset $optionalHeader -Value 0x10B
    $bytes[$optionalHeader + 2] = 14
    Set-UInt32LittleEndian -Offset ($optionalHeader + 4) -Value 0x200
    Set-UInt32LittleEndian -Offset ($optionalHeader + 16) -Value 0x1000
    Set-UInt32LittleEndian -Offset ($optionalHeader + 20) -Value 0x1000
    Set-UInt32LittleEndian -Offset ($optionalHeader + 24) -Value 0x2000
    Set-UInt32LittleEndian -Offset ($optionalHeader + 28) -Value 0x400000
    Set-UInt32LittleEndian -Offset ($optionalHeader + 32) -Value 0x1000
    Set-UInt32LittleEndian -Offset ($optionalHeader + 36) -Value 0x200
    Set-UInt16LittleEndian -Offset ($optionalHeader + 40) -Value 6
    Set-UInt16LittleEndian -Offset ($optionalHeader + 48) -Value 6
    Set-UInt32LittleEndian -Offset ($optionalHeader + 56) -Value 0x2000
    Set-UInt32LittleEndian -Offset ($optionalHeader + 60) -Value 0x200
    Set-UInt16LittleEndian -Offset ($optionalHeader + 68) -Value 3
    Set-UInt32LittleEndian -Offset ($optionalHeader + 72) -Value 0x100000
    Set-UInt32LittleEndian -Offset ($optionalHeader + 76) -Value 0x1000
    Set-UInt32LittleEndian -Offset ($optionalHeader + 80) -Value 0x100000
    Set-UInt32LittleEndian -Offset ($optionalHeader + 84) -Value 0x1000
    Set-UInt32LittleEndian -Offset ($optionalHeader + 92) -Value 16

    $sectionHeader = $optionalHeader + 0xE0
    [Text.Encoding]::ASCII.GetBytes('.text').CopyTo($bytes, $sectionHeader)
    Set-UInt32LittleEndian -Offset ($sectionHeader + 8) -Value 1
    Set-UInt32LittleEndian -Offset ($sectionHeader + 12) -Value 0x1000
    Set-UInt32LittleEndian -Offset ($sectionHeader + 16) -Value 0x200
    Set-UInt32LittleEndian -Offset ($sectionHeader + 20) -Value 0x200
    Set-UInt32LittleEndian -Offset ($sectionHeader + 36) -Value 0x60000020

    $bytes[0x200] = 0xC3
    [IO.File]::WriteAllBytes($Path, $bytes)
}
