将 7-Zip 的 7z.exe 与 7z.dll 放入本目录，以供程序在 Windows 上解压 RAR。

推荐做法：
1) 安装 7-Zip (https://www.7-zip.org/)。
2) 复制安装目录下的 7z.exe 与 7z.dll 到此目录。
3) 重新发布或运行 WeflyUpgradeTool。

程序会优先使用输出目录下的 7z.exe；若不存在，会尝试使用输出目录中的 Tools/7zip/7z.exe。






