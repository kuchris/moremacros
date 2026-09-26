"""Read-only disassembly of selected game functions, using installed source signatures."""
import argparse
import re
from pathlib import Path
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_64

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--exe', required=True, type=Path, help='Path to ffxiv_dx11.exe')
parser.add_argument('--client-structs', required=True, type=Path,
                    help='FFXIVClientStructs source directory containing FFXIV/Client')
parser.add_argument('functions', nargs='*', help='Optional function names to inspect')
args = parser.parse_args()
game = args.exe
src = args.client_structs / 'FFXIV' / 'Client' / 'UI' / 'Misc'
if not game.is_file() or not src.is_dir():
    parser.error('--exe must be a file and --client-structs must contain FFXIV/Client/UI/Misc')
pe = pefile.PE(str(game))
data = pe.get_memory_mapped_image()
base = pe.OPTIONAL_HEADER.ImageBase
dis = Cs(CS_ARCH_X86, CS_MODE_64)
names = ['GetMacro', 'ExecuteSlot', 'GetSlotAppearance', 'GetIconIdForSlot', 'GetDisplayNameForSlot', 'Set', 'IsSlotUsable', 'GetCostValueForSlot', 'IsSlotActionTargetInRange2', 'LoadCostDataForSlot', 'PrepareSlotForRender', 'PopulateIntermediateFromSlot', 'WriteSavedSlot', 'SetAndSaveSlot']
out = Path('artifacts/native-inspection')
out.mkdir(parents=True, exist_ok=True)
names.append('IsInPvPArea')
names.extend(['TryGetMacroIconCommand', 'TryResolveMacroIcon'])
if args.functions: names = args.functions
for file in [*src.glob('Rapture*Module*.cs'), src.parent / 'Shell' / 'RaptureShellModule.cs', src.parent.parent / 'Game' / 'GameMain.cs']:
    text = file.read_text()
    for sig, name in re.findall(r'\[MemberFunction\("([A-Fa-f0-9? ]+)"\)\]\s+public\s+(?:static\s+)?partial\s+[\w*]+\s+(\w+)\(', text):
        if name not in names:
            continue
        pattern = b''.join(b'.' if t == '??' else re.escape(bytes([int(t, 16)])) for t in sig.split())
        hits = list(re.finditer(pattern, data, flags=re.DOTALL))
        targets = set()
        for hit in hits:
            pos = hit.start()
            if data[pos] in [0xE8, 0xE9]:
                pos += 5 + int.from_bytes(data[pos+1:pos+5], 'little', signed=True)
            targets.add(pos)
        if len(targets) != 1:
            print(file.name, name, 'matches', len(hits))
            continue
        pos = targets.pop()
        # Runtime function ranges identify the complete function, including branches after an early return.
        end = pos + 1500
        for fn in pe.DIRECTORY_ENTRY_EXCEPTION:
            if fn.struct.BeginAddress <= pos < fn.struct.EndAddress:
                end = fn.struct.EndAddress
                break
        if name == 'Set': end = pos + 1400
        lines = [f'{i.address:x}: {i.mnemonic} {i.op_str}' for i in dis.disasm(data[pos:end], base + pos)]
        path = out / (file.stem + '-' + name + '.txt')
        path.write_text('\n'.join(lines))
        print(name, hex(base + pos), len(lines), path)
