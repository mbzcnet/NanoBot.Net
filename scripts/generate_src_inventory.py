#!/usr/bin/env python3
# 生成 src 目录下的代码结构清单
# 使用方法: python3 scripts/generate_src_inventory.py

import os
import re
from pathlib import Path
from datetime import datetime
from collections import defaultdict

# 配置
SRC_DIR = "src"
OUTPUT_FILE = "doc.ai/solutions/Source-Inventory.md"

# 正则表达式模式
PATTERNS = {
    'class': re.compile(r'(?:public\s+|internal\s+|private\s+|protected\s+|file\s+)?(?:abstract\s+|sealed\s+|static\s+|partial\s+)*class\s+(\w+)', re.MULTILINE),
    'interface': re.compile(r'(?:public\s+|internal\s+|private\s+|protected\s+)?interface\s+(\w+)', re.MULTILINE),
    'enum': re.compile(r'(?:public\s+|internal\s+|private\s+|protected\s+)?enum\s+(\w+)', re.MULTILINE),
    'record': re.compile(r'(?:public\s+|internal\s+|private\s+|protected\s+)?(?:abstract\s+|sealed\s+)?record\s+(?:struct\s+)?(\w+)', re.MULTILINE),
    'struct': re.compile(r'(?:public\s+|internal\s+|private\s+|protected\s+)?(?:readonly\s+|ref\s+)?struct\s+(\w+)', re.MULTILINE),
    'namespace': re.compile(r'^namespace\s+([\w.]+)', re.MULTILINE)
}

class TypeInfo:
    def __init__(self, name, namespace=None):
        self.name = name
        self.namespace = namespace

    @property
    def full_name(self):
        return f"{self.namespace}.{self.name}" if self.namespace else self.name

    def __eq__(self, other):
        if not isinstance(other, TypeInfo):
            return False
        return self.name == other.name and self.namespace == other.namespace

    def __hash__(self):
        return hash((self.name, self.namespace))

class FileInventory:
    def __init__(self, path):
        self.path = path
        self.classes = []
        self.interfaces = []
        self.enums = []
        self.records = []
        self.structs = []

    def has_any_definitions(self):
        return any([self.classes, self.interfaces, self.enums, self.records, self.structs])

class ProjectInventory:
    def __init__(self, name, path):
        self.name = name
        self.path = path
        self.files = []

def extract_types(content):
    """从文件内容中提取类型定义"""
    # 提取 namespace (取第一个)
    ns_match = PATTERNS['namespace'].search(content)
    namespace = ns_match.group(1) if ns_match else None

    types = {
        'classes': [],
        'interfaces': [],
        'enums': [],
        'records': [],
        'structs': []
    }

    for match in PATTERNS['class'].finditer(content):
        types['classes'].append(TypeInfo(match.group(1), namespace))

    for match in PATTERNS['interface'].finditer(content):
        types['interfaces'].append(TypeInfo(match.group(1), namespace))

    for match in PATTERNS['enum'].finditer(content):
        types['enums'].append(TypeInfo(match.group(1), namespace))

    for match in PATTERNS['record'].finditer(content):
        types['records'].append(TypeInfo(match.group(1), namespace))

    for match in PATTERNS['struct'].finditer(content):
        types['structs'].append(TypeInfo(match.group(1), namespace))

    # 去重
    for key in types:
        types[key] = list(dict.fromkeys(types[key]))

    return types

def scan_src():
    """扫描 src 目录"""
    project_inventory = []

    # 获取脚本所在目录和项目根目录
    script_dir = Path(__file__).parent.parent
    src_path = script_dir / SRC_DIR

    if not src_path.exists():
        print(f"错误: 目录 {src_path} 不存在")
        return project_inventory

    # 查找所有 .csproj 文件
    csproj_files = sorted(src_path.rglob("*.csproj"))
    print(f"找到 {len(csproj_files)} 个项目\n")

    for proj_path in csproj_files:
        proj_dir = proj_path.parent
        proj_name = proj_path.stem
        relative_proj_path = str(proj_path.relative_to(script_dir))

        project = ProjectInventory(proj_name, relative_proj_path)

        # 获取项目中的所有 .cs 文件（排除 obj 和 bin）
        cs_files = [
            f for f in proj_dir.rglob("*.cs")
            if "obj" not in f.parts and "bin" not in f.parts
        ]

        for cs_file in sorted(cs_files):
            relative_file_path = str(cs_file.relative_to(proj_dir))
            content = cs_file.read_text(encoding='utf-8', errors='ignore')

            file_inventory = FileInventory(relative_file_path)
            types = extract_types(content)

            file_inventory.classes = types['classes']
            file_inventory.interfaces = types['interfaces']
            file_inventory.enums = types['enums']
            file_inventory.records = types['records']
            file_inventory.structs = types['structs']

            if file_inventory.has_any_definitions():
                project.files.append(file_inventory)

        if project.files:
            project_inventory.append(project)

    return project_inventory

def generate_markdown(project_inventory):
    """生成 Markdown 报告"""
    lines = []

    lines.append("# Source Code Inventory")
    lines.append("")
    lines.append(f"生成时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    lines.append("")
    lines.append(f"扫描路径: `{SRC_DIR}/`")
    lines.append("")

    # 项目概览
    lines.append("## 项目概览")
    lines.append("")
    lines.append("| 项目名称 | 文件数 | 类 | 接口 | 枚举 | Record | 结构体 |")
    lines.append("|----------|--------|-----|------|------|--------|--------|")

    total_stats = {'files': 0, 'classes': 0, 'interfaces': 0, 'enums': 0, 'records': 0, 'structs': 0}

    for proj in project_inventory:
        file_count = len(proj.files)
        class_count = sum(len(f.classes) for f in proj.files)
        interface_count = sum(len(f.interfaces) for f in proj.files)
        enum_count = sum(len(f.enums) for f in proj.files)
        record_count = sum(len(f.records) for f in proj.files)
        struct_count = sum(len(f.structs) for f in proj.files)

        total_stats['files'] += file_count
        total_stats['classes'] += class_count
        total_stats['interfaces'] += interface_count
        total_stats['enums'] += enum_count
        total_stats['records'] += record_count
        total_stats['structs'] += struct_count

        lines.append(f"| {proj.name} | {file_count} | {class_count} | {interface_count} | {enum_count} | {record_count} | {struct_count} |")

    lines.append("")

    # 详细清单
    lines.append("## 详细清单")
    lines.append("")

    for proj in project_inventory:
        lines.append(f"### {proj.name}")
        lines.append("")
        lines.append(f"**项目文件:** `{proj.path}`")
        lines.append("")

        # 按文件夹分组
        files_by_dir = defaultdict(list)
        for f in proj.files:
            dir_name = str(Path(f.path).parent)
            if dir_name == ".":
                dir_name = "(根目录)"
            files_by_dir[dir_name].append(f)

        for dir_name in sorted(files_by_dir.keys()):
            lines.append(f"#### {dir_name}/")
            lines.append("")

            for file in sorted(files_by_dir[dir_name], key=lambda x: Path(x.path).name):
                lines.append(f"**{Path(file.path).name}**")

                items = []
                for t in sorted(file.classes, key=lambda x: x.name):
                    items.append(f"`class {t.name}`")
                for t in sorted(file.interfaces, key=lambda x: x.name):
                    items.append(f"`interface {t.name}`")
                for t in sorted(file.enums, key=lambda x: x.name):
                    items.append(f"`enum {t.name}`")
                for t in sorted(file.records, key=lambda x: x.name):
                    items.append(f"`record {t.name}`")
                for t in sorted(file.structs, key=lambda x: x.name):
                    items.append(f"`struct {t.name}`")

                if items:
                    lines.append(f"- {', '.join(items)}")
                lines.append("")

        lines.append("")

    # 统计汇总
    lines.append("---")
    lines.append("")
    lines.append("## 统计汇总")
    lines.append("")

    total_types = (total_stats['classes'] + total_stats['interfaces'] +
                   total_stats['enums'] + total_stats['records'] + total_stats['structs'])

    lines.append("| 指标 | 数量 |")
    lines.append("|------|------|")
    lines.append(f"| 项目数 | {len(project_inventory)} |")
    lines.append(f"| 文件数 | {total_stats['files']} |")
    lines.append(f"| 类 (Class) | {total_stats['classes']} |")
    lines.append(f"| 接口 (Interface) | {total_stats['interfaces']} |")
    lines.append(f"| 枚举 (Enum) | {total_stats['enums']} |")
    lines.append(f"| Record | {total_stats['records']} |")
    lines.append(f"| 结构体 (Struct) | {total_stats['structs']} |")
    lines.append(f"| **类型总计** | **{total_types}** |")
    lines.append("")

    # 按命名空间分组
    lines.append("### 按命名空间分组")
    lines.append("")

    all_types = defaultdict(list)
    for proj in project_inventory:
        for f in proj.files:
            for t in f.classes:
                all_types[t.namespace or "(全局)"].append((t, "class", proj.name))
            for t in f.interfaces:
                all_types[t.namespace or "(全局)"].append((t, "interface", proj.name))
            for t in f.enums:
                all_types[t.namespace or "(全局)"].append((t, "enum", proj.name))
            for t in f.records:
                all_types[t.namespace or "(全局)"].append((t, "record", proj.name))
            for t in f.structs:
                all_types[t.namespace or "(全局)"].append((t, "struct", proj.name))

    for ns in sorted(all_types.keys()):
        lines.append(f"#### {ns}")
        lines.append("")

        sorted_types = sorted(all_types[ns], key=lambda x: x[0].name)
        for type_info, kind, proj_name in sorted_types:
            lines.append(f"- `{kind} {type_info.name}` ({proj_name})")
        lines.append("")

    return "\n".join(lines)

def main():
    # 获取脚本所在目录和项目根目录
    script_dir = Path(__file__).parent.parent
    src_path = script_dir / SRC_DIR

    print(f"扫描目录: {src_path}")

    project_inventory = scan_src()

    if not project_inventory:
        print("未找到任何项目或代码文件")
        return

    markdown_content = generate_markdown(project_inventory)

    # 保存文件
    output_path = script_dir / OUTPUT_FILE
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(markdown_content, encoding='utf-8')

    print(f"\n清单已生成: {output_path}")
    print("\n=== 统计 ===")

    total_files = sum(len(p.files) for p in project_inventory)
    total_classes = sum(sum(len(f.classes) for f in p.files) for p in project_inventory)
    total_interfaces = sum(sum(len(f.interfaces) for f in p.files) for p in project_inventory)
    total_enums = sum(sum(len(f.enums) for f in p.files) for p in project_inventory)
    total_records = sum(sum(len(f.records) for f in p.files) for p in project_inventory)
    total_structs = sum(sum(len(f.structs) for f in p.files) for p in project_inventory)
    total_types = total_classes + total_interfaces + total_enums + total_records + total_structs

    print(f"  项目数: {len(project_inventory)}")
    print(f"  文件数: {total_files}")
    print(f"  类 (Class): {total_classes}")
    print(f"  接口 (Interface): {total_interfaces}")
    print(f"  枚举 (Enum): {total_enums}")
    print(f"  Record: {total_records}")
    print(f"  结构体 (Struct): {total_structs}")
    print(f"  类型总计: {total_types}")

if __name__ == "__main__":
    main()
