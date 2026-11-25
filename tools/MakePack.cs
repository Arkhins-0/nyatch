#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property PublishAot=false
#:project ../src/Nyatch.Core/Nyatch.Core.csproj

// Make a character pack's .nyatch file, and list it.
//
//   dotnet run tools\MakePack.cs -- <pack folder> <home folder>
//
// The theme folder holds pack.json (id, name, version, author, description, accent, font), preview.png (or
// preview.gif: the cover shown before downloading), its font if it has one, and the pictures, each named for the
// moment it is shown in (idle.gif, time-morning.png...). The home folder is the "nyatch-themes" branch as
// checked out (or any folder, to try a pack): the tool writes <id>\<id>.nyatch and <id>\preview.png
// there and adds the pack to its index.json, which is the list Nyatch shows.

using Nyatch.Core;

if (args.Length != 2)
{
    Console.Error.WriteLine("Use: dotnet run tools\\MakePack.cs -- <pack folder> <home folder>");
    return 1;
}
try
{
    var pack = CharacterPacks.Make(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));
    Console.WriteLine($"{pack.Name} {pack.Version}: {pack.Pictures} pictures, {pack.Size / 1024.0 / 1024.0:0.0} MB");
    Console.WriteLine($"  {Path.Combine(Path.GetFullPath(args[1]), pack.File.Replace('/', Path.DirectorySeparatorChar))}");
    Console.WriteLine($"  own pictures for: {(pack.Covers is { Length: > 0 } covers ? string.Join(", ", covers) : "none (general pictures only)")}");
    return 0;
}
catch (Exception error) when (error is InvalidDataException or IOException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
