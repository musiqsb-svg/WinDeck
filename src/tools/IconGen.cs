using System;
using System.IO;

// 빌드 도우미: WinDeck.AppIcon 으로 exe 아이콘 파일(.ico)을 만든다.
static class IconGen
{
    static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: IconGen <output.ico>");
            return 1;
        }
        File.WriteAllBytes(args[0], WinDeck.AppIcon.BuildIco(new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 }));
        return 0;
    }
}
