# Sumthing

A calculator for Windows 10 and 11, by Bytehawk Studio. It does what the Windows calculator does and adds a paper tape, sums you can type in words, a unit and currency converter, and a few everyday tools.

**Download:** the latest **Sumthing-x.y.z-Setup.exe** (installer) or **Sumthing-x.y.z-win-x64.zip** (portable) from [Releases](../../releases), or install it from the **Bytehawk Studio Launcher**, which keeps it up to date.

## What's in it

| | |
|---|---|
| **Standard** | The everyday calculator: memory, history, %, x², √x, 1/x, +/−. Exact decimal maths, so 0.1 + 0.2 = 0.3. |
| **Three looks** | Modern **Light** and **Dark** (or follow Windows), with seven accent colours, and **Classic**, the Windows 7 calculator with its View / Edit / Help menus. |
| **Tape** | A paper roll beside the calculator that prints every step like an adding machine. Click beside a line to add a note; copy, save (.txt) or print it. |
| **Type-a-sum** | A notepad where every line is a sum, answered as you type: `12% of 340`, `80 + 15%`, `20% off 80`, `rent = 1,250` then `rent * 12`, `total`, `sqrt 81`, `5k`. Click an answer to copy it. |
| **Converter** | Length, weight, temperature, volume, area, speed, time, data, fuel economy and **currency** (daily European Central Bank rates via the free [Frankfurter](https://frankfurter.dev) service, kept for offline use). Shows the amount in every unit at once. |
| **Everyday tools** | Tip and bill splitter, percent change, discount and sales tax, date difference (with weekdays, and adding or taking away days). |

## Keyboard

| Keys | Does |
|---|---|
| `0`–`9` `.` `+` `-` `*` `/` `%` | Numbers and operators |
| `Enter` / `=` | Equals |
| `Backspace`, `Delete`, `Esc` | Delete a digit, clear entry (CE), clear (C) |
| `F9`, `R`, `Q`, `@` | +/−, 1/x, x², √x |
| `Ctrl+M` `Ctrl+R` `Ctrl+L` `Ctrl+P` `Ctrl+Q` | Memory store, recall, clear, add, subtract |
| `Ctrl+C`, `Ctrl+V` | Copy the result; paste a number or a whole sum |
| `Ctrl+1`–`Ctrl+4` | Standard, Type-a-sum, Converter, Everyday tools |
| `Ctrl+T`, `Ctrl+H`, `Ctrl+,` | Tape, history, settings |

## Built with

C# / WPF on .NET 8, published as one self-contained `Sumthing.exe` (no .NET install needed), with an Inno Setup installer. Same setup as [Draftly](https://github.com/adamglassner/draftly). See [BUILD.md](BUILD.md) to build it yourself.

Settings, history, memory, the tape and your Type-a-sum sheet are kept in `%APPDATA%\Sumthing`. Uninstalling leaves them there.

`mockups/` holds the original design mockup.

© 2026 Bytehawk Studio
