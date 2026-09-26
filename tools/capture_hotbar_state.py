"""Read-only, bounded snapshot of native hotbar UI fields; no process writes or game calls."""
import argparse
import ctypes as ct
import json
import re
import struct
from pathlib import Path
import pefile

p = argparse.ArgumentParser()
p.add_argument('--pid', type=int, required=True)
p.add_argument('--base', type=int, required=True)
p.add_argument('--exe', required=True)
p.add_argument('--expect-bar', type=int)
p.add_argument('--expect-slot', type=int)
p.add_argument('--expect-id', type=int)
p.add_argument('--expect-icon', type=int)
p.add_argument('--check-glow', action='store_true')
p.add_argument('--sample-motion', type=float, default=0)
p.add_argument('--catalog', action='store_true')
args = p.parse_args()
k32 = ct.WinDLL('kernel32', use_last_error=True)
k32.OpenProcess.argtypes = [ct.c_ulong, ct.c_int, ct.c_ulong]
k32.OpenProcess.restype = ct.c_void_p
k32.ReadProcessMemory.argtypes = [ct.c_void_p, ct.c_void_p, ct.c_void_p, ct.c_size_t, ct.POINTER(ct.c_size_t)]
k32.CloseHandle.argtypes = [ct.c_void_p]
handle = k32.OpenProcess(0x10, False, args.pid)  # PROCESS_VM_READ only
if not handle:
    raise ct.WinError(ct.get_last_error())

def read(address, size):
    if not 0 < size <= 0xA000 or address < 0x10000:
        raise ValueError('Invalid bounded read')
    buf = ct.create_string_buffer(size)
    actual = ct.c_size_t()
    if not k32.ReadProcessMemory(handle, address, buf, size, ct.byref(actual)) or actual.value != size:
        raise ct.WinError(ct.get_last_error())
    return buf.raw

def value(address, fmt):
    return struct.unpack(fmt, read(address, struct.calcsize(fmt)))[0]

try:
    image = pefile.PE(args.exe, fast_load=True).get_memory_mapped_image()
    def singleton(signature):
        regex = b''.join(b'.' if b == '??' else re.escape(bytes([int(b, 16)])) for b in signature.split())
        matches = list(re.finditer(regex, image, re.DOTALL))
        if len(matches) != 1:
            raise ValueError('Signature must resolve exactly once')
        rva = matches[0].start()
        pointer = args.base + rva + 7 + struct.unpack_from('<i', image, rva + 3)[0]
        return value(pointer, '<Q')

    stage = singleton('48 8B 05 ?? ?? ?? ?? 4C 8B 40 18 45 8B 40 18')
    framework = singleton('48 8B 1D ?? ?? ?? ?? 8B 7C 24')
    ui = value(framework + 0x2B68, '<Q')
    if args.catalog:
        first, end = struct.unpack('<QQ', read(ui + 0x1748 + 0x30, 16))
        assert 0 <= (end-first)//8 <= 256
        total = 0
        samples = []
        mismatches = 0
        for n in range((end-first)//8):
            category = value(first+n*8, '<Q')
            if not category:
                continue
            group = value(category+0xB8, '<B')
            if group >= 200:  # Exclude custom plugin commands and player-defined categories.
                continue
            text_start, text_end = struct.unpack('<QQ',read(category+8,16))
            data_start, data_end = struct.unpack('<QQ',read(category+0x20,16))
            count = min((text_end-text_start)//8,(data_end-data_start)//8)
            assert 0 <= count <= 30000
            total += count
            for j in range(count):
                entry = struct.unpack('<HHI',read(data_start+j*8,8))
                mismatches += entry[0] != group
                pointer = value(text_start+j*8,'<Q')
                raw = read(pointer, min(256, 4096-(pointer%4096))).split(b'\0')[0]
                if raw.lower() == b'hello!':
                    samples.append({'categoryGroup':group, 'entry':entry, 'text':raw.decode()})
        print('Native completion catalog:',{'categories':(end-first)//8,'entries':total,'groupMismatches':mismatches,'Hello':samples})
    hotbars = ui + 0x57C60
    manager = value(stage + 0x20, '<Q')
    unit_list = read(manager + 0x6900, 0x810)
    count = struct.unpack_from('<H', unit_list, 0x808)[0]
    if count > 256:
        raise ValueError('Unexpected addon count')
    result = {'ModuleReady': bool(value(hotbars + 0x58, '<B')), 'Bars': []}
    macro_addon = 0
    for i in range(count):
        addon = struct.unpack_from('<Q', unit_list, 8 + i * 8)[0]
        if not addon:
            continue
        label = read(addon + 8, 32).split(b'\0')[0].decode('ascii', errors='replace')
        if label == 'Macro':
            macro_addon = addon
        if not re.fullmatch(r'_ActionBar(?:0[1-9])?', label):
            continue
        data = read(addon, 0x260)
        first, end = struct.unpack_from('<QQ', data, 0x238)
        num = (end - first) // 0xC8
        if not 0 <= num <= 16:
            raise ValueError('Unexpected slot vector count')
        bar_id = data[0x254]
        bar = {'Name': label, 'Ready': bool(data[0x1A1] & 1),
               'Visibility': (struct.unpack_from('<I', data, 0x198)[0] >> 20) & 15,
               'BarId': bar_id, 'SlotCount': data[0x256], 'VectorCount': num,
               'Locked': bool(data[0x257]), 'Cross': bool(data[0x25A]), 'Pet': bool(data[0x25D]), 'Slots': []}
        for index in range(min(num, data[0x256], 12)):
            component = value(first + index * 0xC8, '<Q')
            node = value(component + 0xA8, '<Q') if component else 0
            slot = {'Index': index, 'HasComponent': bool(component), 'HasNode': bool(node)}
            if node:
                nd = read(node, 0xB0)
                slot.update(Transform=struct.unpack_from('<4f', nd, 0x60), Position=struct.unpack_from('<2f', nd, 0x70),
                            Size=struct.unpack_from('<2H', nd, 0xA0), Flags=struct.unpack_from('<H', nd, 0xAE)[0])
            if bar_id < 18:
                native = hotbars + 0xA0 + (bar_id * 16 + index) * 0xE8
                slot['CommandType'] = value(native + 0xC7, '<B')
                slot['CommandId'] = value(native + 0xB8, '<I')
            bar['Slots'].append(slot)
        result['Bars'].append(bar)
    output = Path('artifacts/native-inspection/live-hotbars.json')
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2))
    if args.check_glow:
        holder = value(stage + 0x38, '<Q')
        arrays = value(holder + 0x18, '<Q')
        action_array = value(arrays + 7*8, '<Q')
        numbers = value(action_array + 0x28, '<Q')
        linked = []
        for bar in result['Bars']:
            for slot in bar['Slots']:
                if slot.get('CommandType') != 10 or slot['CommandId'] >> 24 != 0x4D:
                    continue
                fields = struct.unpack('<17i', read(numbers + (15 + bar['BarId']*272 + slot['Index']*17)*4, 68))
                item = {'bar':bar['BarId']+1, 'slot':slot['Index']+1, 'glow':fields[14], 'pulse':fields[15], 'icon':fields[4]}
                linked.append(item)
        print('Linked macro animation:', linked)
        assert linked and all(s['glow'] == 0 and s['pulse'] == 0 for s in linked), 'Saved macros must not show skill proc glow/pulse'
    if args.sample_motion and macro_addon:
        import time
        node = value(macro_addon + 0xC8, '<Q')
        samples = []
        start = time.perf_counter()
        while time.perf_counter() - start < min(args.sample_motion, 60):
            logical = struct.unpack('<hh', read(macro_addon + 0x1D4, 4))
            rendered = struct.unpack('<ff', read(node + 0x70, 8))
            samples.append([time.perf_counter() - start, *logical, *rendered])
            time.sleep(.008)
        Path('artifacts/native-inspection/macro-motion.json').write_text(json.dumps(samples))
        differences = [s for s in samples if abs(s[1]-s[3]) > 2 or abs(s[2]-s[4]) > 2]
        print('Motion:', {'samples': len(samples), 'positions': len(set((s[1],s[2]) for s in samples)), 'mismatches':len(differences), 'examples':differences[:3]})
    for bar in result['Bars']:
        empty = [s['Index'] + 1 for s in bar['Slots'] if s.get('CommandType') == 0]
        print(f"Bar {bar['BarId']+1}: ready={bar['Ready']} visible={bar['Visibility']} locked={bar['Locked']} empty={empty}")
    if args.expect_bar is not None:
        if not 1 <= args.expect_bar <= 10 or not 1 <= args.expect_slot <= 12:
            raise ValueError('Invalid target')
        target = hotbars + 0xA0 + ((args.expect_bar-1)*16 + args.expect_slot-1)*0xE8
        actual_type = value(target + 0xC7, '<B')
        actual_id = value(target + 0xB8, '<I')
        job = value(hotbars + 0x59, '<B')
        shared = bool(value(hotbars + 0x88, '<I') & (1 << (args.expect_bar-1)))
        group = 0 if shared else job
        saved = hotbars + 0x1238C + ((group*18 + args.expect_bar-1)*16 + args.expect_slot-1)*5
        print('Expected active slot:', {'type': 10, 'id': args.expect_id})
        print('Actual active slot:', {'type': actual_type, 'id': actual_id})
        print('Saved slot:', {'type': value(saved, '<B'), 'id': value(saved+1, '<I')})
        assert (actual_type, actual_id) == (10, args.expect_id), 'Hotbar placement could not be confirmed: live slot mismatch'
        if args.expect_icon is not None:
            actual_icon = value(target + 0xD0, '<I')
            holder = value(stage + 0x38, '<Q')
            arrays = value(holder + 0x18, '<Q')
            numbers = value(value(arrays + 7*8, '<Q') + 0x28, '<Q')
            drawn_icon = value(numbers + (15 + (args.expect_bar-1)*272 + (args.expect_slot-1)*17 + 4)*4, '<I')
            print('Icon:', {'expected':args.expect_icon, 'slot':actual_icon, 'drawn':drawn_icon})
            assert actual_icon == drawn_icon == args.expect_icon, 'The hotbar does not display the /micon icon'
finally:
    k32.CloseHandle(handle)
