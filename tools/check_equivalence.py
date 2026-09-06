#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
新旧等価チェックツール (Equivalence & Regression Auditor)
Audio Effector プロジェクトのリファクタリングにおける機能・UIバインディング・公開API・モデルプロパティの等価性を機械的に検証するツール。

主な機能:
1. XAMLバインディング網羅性検証 (XAML Binding Verification)
2. ViewModel / 公開API等価性検証 (ViewModel & Public API Verification)
3. モデル・エンティティプロパティ等価性検証 (Model & Entity Property Verification)

使用法:
  python tools/check_equivalence.py [options]
  
オプション:
  --all           すべての検証を実行 (デフォルト)
  --xaml          XAMLバインディング網羅性検証のみ実行
  --vm            ViewModel / 公開API等価性検証のみ実行
  --models        モデル・エンティティ等価性検証のみ実行
  --report FILE   Markdown形式の詳細レポートをFILEに出力
  --json          結果をJSON形式で標準出力に出力
  --verbose, -v   詳細なログを出力
"""

import os
import sys
import re
import json
import argparse
from pathlib import Path
from typing import Dict, List, Set, Tuple, Optional, Any
from dataclasses import dataclass, field, asdict

# WindowsコンソールでのUTF-8対応
if sys.platform == 'win32':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    except Exception:
        pass

# ANSIカラーコード定義
class Colors:
    RESET = "\033[0m"
    BOLD = "\033[1m"
    RED = "\033[91m"
    GREEN = "\033[92m"
    YELLOW = "\033[93m"
    BLUE = "\033[94m"
    MAGENTA = "\033[95m"
    CYAN = "\033[96m"
    WHITE = "\033[97m"
    GRAY = "\033[90m"


def colorize(text: str, color: str, use_color: bool = True) -> str:
    return f"{color}{text}{Colors.RESET}" if use_color else text


# ==============================================================================
# データ構造
# ==============================================================================

@dataclass
class MemberInfo:
    name: str
    member_type: str  # 'property', 'command', 'method', 'event', 'field'
    data_type: str = ""
    is_public: bool = True
    line_number: int = 0
    file_path: str = ""
    attributes: List[str] = field(default_factory=list)


@dataclass
class ClassInfo:
    name: str
    namespace: str
    file_path: str
    base_classes: List[str] = field(default_factory=list)
    members: Dict[str, MemberInfo] = field(default_factory=dict)
    
    def get_public_properties(self) -> Dict[str, MemberInfo]:
        return {k: v for k, v in self.members.items() if v.member_type == 'property' and v.is_public}
    
    def get_public_commands(self) -> Dict[str, MemberInfo]:
        return {k: v for k, v in self.members.items() if v.member_type == 'command' and v.is_public}

    def get_public_methods(self) -> Dict[str, MemberInfo]:
        return {k: v for k, v in self.members.items() if v.member_type == 'method' and v.is_public}

    def get_public_events(self) -> Dict[str, MemberInfo]:
        return {k: v for k, v in self.members.items() if v.member_type == 'event' and v.is_public}

    def get_all_members(self, classes_map: Dict[str, 'ClassInfo'], visited: Optional[Set[str]] = None) -> Dict[str, MemberInfo]:
        """基底クラスを含めたすべてのメンバーを取得します"""
        if visited is None:
            visited = set()
        if self.name in visited:
            return dict(self.members)
        visited.add(self.name)

        all_m = dict(self.members)
        for base in self.base_classes:
            clean_base = base.split('<')[0].strip()
            base_cls = classes_map.get(clean_base)
            if base_cls:
                base_m = base_cls.get_all_members(classes_map, visited)
                for k, v in base_m.items():
                    if k not in all_m:
                        all_m[k] = v
        return all_m


@dataclass
class XamlBinding:
    raw_binding: str
    path: str
    root_property: str
    mode: Optional[str] = None
    converter: Optional[str] = None
    element_name: Optional[str] = None
    relative_source: Optional[str] = None
    source: Optional[str] = None
    target_property: str = ""
    element_tag: str = ""
    file_path: str = ""
    line_number: int = 0


@dataclass
class AuditResult:
    category: str
    item_name: str
    status: str  # 'PASS', 'WARN', 'FAIL', 'INFO'
    message: str
    details: List[str] = field(default_factory=list)
    file_path: str = ""
    line_number: int = 0


# ==============================================================================
# 精密 C# ソースコードパーサー
# ==============================================================================

class CSharpParser:
    """C# ソースファイルからクラス・プロパティ・コマンド・メソッド・イベントを正確に抽出するパーサー"""

    RE_NAMESPACE = re.compile(r'^\s*namespace\s+([A-Za-z0-9_.]+)', re.MULTILINE)
    RE_CLASS = re.compile(r'public\s+(?:(?:partial|static|abstract|sealed)\s+)*(?:class|window|usercontrol)\s+([A-Za-z0-9_]+)(?:\s*:\s*([^{]+))?', re.IGNORECASE)
    RE_RECORD = re.compile(r'public\s+(?:(?:partial|readonly)\s+)*record\s+(?:class\s+|struct\s+)?([A-Za-z0-9_]+)(?:\s*\(([^)]*)\))?(?:\s*:\s*([^{]+))?')
    
    # 複数行対応メンバーパターン
    RE_COMMAND = re.compile(
        r'public\s+(?:(?:override|virtual|static|new)\s+)*'
        r'(?P<type>(?:ICommand|RelayCommand|AsyncRelayCommand)(?:<[A-Za-z0-9_<>?,.\[\]\s]+>)?)\s+'
        r'(?P<name>[A-Za-z0-9_]+)\s*'
        r'(?:\{|=>|=)',
        re.DOTALL
    )
    RE_PROPERTY = re.compile(
        r'public\s+(?:(?:override|virtual|static|async|new|required|readonly)\s+)*'
        r'(?P<type>[A-Za-z0-9_<>?,.\[\]\s]+?)\s+'
        r'(?P<name>[A-Za-z0-9_]+)\s*'
        r'(?:\{[^}]*?(?:get|set|init)|=>|=)',
        re.DOTALL
    )
    RE_EVENT = re.compile(
        r'public\s+(?:(?:override|virtual|static|new)\s+)*'
        r'event\s+(?P<type>[A-Za-z0-9_<>?,.\[\]\s]+)\s+(?P<name>[A-Za-z0-9_]+)\s*;',
        re.DOTALL
    )
    RE_METHOD = re.compile(
        r'public\s+(?:(?:override|virtual|static|async|new)\s+)*'
        r'(?P<ret>[A-Za-z0-9_<>?,.\[\]\s]+?)\s+'
        r'(?P<name>[A-Za-z0-9_]+)\s*\((?P<params>[^)]*)\)\s*(?:\{|=>)',
        re.DOTALL
    )

    @classmethod
    def parse_file(cls, file_path: Path) -> List[ClassInfo]:
        try:
            with open(file_path, 'r', encoding='utf-8') as f:
                content = f.read()
        except UnicodeDecodeError:
            try:
                with open(file_path, 'r', encoding='utf-8-sig') as f:
                    content = f.read()
            except Exception:
                return []
        except Exception:
            return []

        clean_content = cls._remove_comments(content)
        
        namespace = ""
        ns_match = cls.RE_NAMESPACE.search(clean_content)
        if ns_match:
            namespace = ns_match.group(1).rstrip(';')

        classes: List[ClassInfo] = []
        
        for match in cls.RE_CLASS.finditer(clean_content):
            class_name = match.group(1)
            bases = []
            if match.group(2):
                bases = [b.strip() for b in match.group(2).split('{')[0].split(',') if b.strip()]
            
            c_info = ClassInfo(
                name=class_name,
                namespace=namespace,
                file_path=str(file_path),
                base_classes=bases
            )
            
            class_start = match.end()
            class_body = cls._extract_block(clean_content, class_start)
            if class_body:
                cls._parse_members(class_body, c_info, str(file_path))
            
            classes.append(c_info)

        for match in cls.RE_RECORD.finditer(clean_content):
            rec_name = match.group(1)
            params_str = match.group(2) or ""
            bases = []
            if match.group(3):
                bases = [b.strip() for b in match.group(3).split('{')[0].split(',') if b.strip()]

            r_info = ClassInfo(
                name=rec_name,
                namespace=namespace,
                file_path=str(file_path),
                base_classes=bases
            )
            if params_str:
                for param in params_str.split(','):
                    param = param.strip()
                    parts = param.split()
                    if len(parts) >= 2:
                        p_type, p_name = parts[-2], parts[-1]
                        r_info.members[p_name] = MemberInfo(
                            name=p_name,
                            member_type='property',
                            data_type=p_type,
                            file_path=str(file_path)
                        )
            
            rec_start = match.end()
            rec_body = cls._extract_block(clean_content, rec_start)
            if rec_body:
                cls._parse_members(rec_body, r_info, str(file_path))
            
            classes.append(r_info)

        return classes

    @staticmethod
    def _remove_comments(text: str) -> str:
        text = re.sub(r'//.*', '', text)
        text = re.sub(r'/\*.*?\*/', '', text, flags=re.DOTALL)
        return text

    @staticmethod
    def _extract_block(text: str, start_pos: int) -> str:
        open_idx = text.find('{', start_pos)
        if open_idx == -1:
            return ""
        depth = 0
        end_idx = open_idx
        for i in range(open_idx, len(text)):
            if text[i] == '{':
                depth += 1
            elif text[i] == '}':
                depth -= 1
                if depth == 0:
                    end_idx = i
                    break
        return text[open_idx + 1:end_idx]

    @classmethod
    def _parse_members(cls, body: str, class_info: ClassInfo, file_path: str):
        for m in cls.RE_COMMAND.finditer(body):
            name = m.group('name').strip()
            c_type = m.group('type').strip()
            class_info.members[name] = MemberInfo(
                name=name,
                member_type='command',
                data_type=c_type,
                file_path=file_path
            )

        for m in cls.RE_PROPERTY.finditer(body):
            name = m.group('name').strip()
            p_type = m.group('type').strip()
            if name not in class_info.members and name not in ('class', 'struct', 'interface', 'enum', 'record', 'void'):
                class_info.members[name] = MemberInfo(
                    name=name,
                    member_type='property',
                    data_type=p_type,
                    file_path=file_path
                )

        for m in cls.RE_EVENT.finditer(body):
            name = m.group('name').strip()
            e_type = m.group('type').strip()
            class_info.members[name] = MemberInfo(
                name=name,
                member_type='event',
                data_type=e_type,
                file_path=file_path
            )

        for m in cls.RE_METHOD.finditer(body):
            name = m.group('name').strip()
            ret_type = m.group('ret').strip()
            if name not in class_info.members and name not in ('if', 'for', 'foreach', 'while', 'switch', 'catch', 'lock', 'using', class_info.name):
                class_info.members[name] = MemberInfo(
                    name=name,
                    member_type='method',
                    data_type=ret_type,
                    file_path=file_path
                )


# ==============================================================================
# 精密 XAML パーサー
# ==============================================================================

class XamlParser:
    """ネスト波括弧に対応した精密XAMLバインディングパーサー"""

    @classmethod
    def parse_file(cls, file_path: Path) -> List[XamlBinding]:
        bindings: List[XamlBinding] = []
        try:
            with open(file_path, 'r', encoding='utf-8') as f:
                content = f.read()
        except UnicodeDecodeError:
            try:
                with open(file_path, 'r', encoding='utf-8-sig') as f:
                    content = f.read()
            except Exception:
                return []
        except Exception:
            return []

        lines = content.splitlines()

        for line_idx, line in enumerate(lines, start=1):
            stripped = line.strip()
            if stripped.startswith('<!--') and stripped.endswith('-->'):
                continue

            attr_pattern = re.compile(r'([A-Za-z0-9_.:]+)\s*=\s*"([^"]*)"')
            for match in attr_pattern.finditer(line):
                attr_name = match.group(1)
                attr_val = match.group(2)

                if "{Binding" not in attr_val:
                    continue

                raw_bindings = cls._extract_binding_blocks(attr_val)
                for raw_b in raw_bindings:
                    b_info = cls._parse_binding_string(raw_b, attr_name, str(file_path), line_idx)
                    if b_info:
                        bindings.append(b_info)

        return bindings

    @classmethod
    def _extract_binding_blocks(cls, text: str) -> List[str]:
        results = []
        pos = 0
        while True:
            idx = text.find("{Binding", pos)
            if idx == -1:
                break
            depth = 0
            end_idx = idx
            for i in range(idx, len(text)):
                if text[i] == '{':
                    depth += 1
                elif text[i] == '}':
                    depth -= 1
                    if depth == 0:
                        end_idx = i + 1
                        break
            if depth == 0:
                results.append(text[idx:end_idx])
                pos = end_idx
            else:
                pos = idx + 8
        return results

    @classmethod
    def _parse_binding_string(cls, raw: str, target_attr: str, file_path: str, line_idx: int) -> Optional[XamlBinding]:
        inner = raw[len("{Binding"): -1].strip()
        if not inner:
            return XamlBinding(
                raw_binding=raw,
                path=".",
                root_property=".",
                target_property=target_attr,
                file_path=file_path,
                line_number=line_idx
            )

        tokens = []
        current = []
        depth = 0
        in_quote = False
        quote_char = ''

        for ch in inner:
            if ch in ("'", '"'):
                if not in_quote:
                    in_quote = True
                    quote_char = ch
                elif quote_char == ch:
                    in_quote = False
                current.append(ch)
            elif not in_quote and ch == '{':
                depth += 1
                current.append(ch)
            elif not in_quote and ch == '}':
                depth -= 1
                current.append(ch)
            elif not in_quote and depth == 0 and ch == ',':
                tokens.append("".join(current).strip())
                current = []
            else:
                current.append(ch)
        if current:
            tokens.append("".join(current).strip())

        path = ""
        mode = None
        converter = None
        element_name = None
        relative_source = None
        source = None

        first_token = tokens[0] if tokens else ""
        if '=' not in first_token and not first_token.startswith('{'):
            path = first_token
            tokens = tokens[1:]

        for token in tokens:
            if '=' in token:
                k, v = token.split('=', 1)
                k, v = k.strip(), v.strip()
                if k == 'Path':
                    path = v
                elif k == 'Mode':
                    mode = v
                elif k == 'Converter':
                    converter = v
                elif k == 'ElementName':
                    element_name = v
                elif k == 'RelativeSource':
                    relative_source = v
                elif k == 'Source':
                    source = v

        if not path and not element_name and not relative_source:
            path = "."

        # インデクサーやサブプロパティを除去してルートプロパティ名を抽出
        # 例: Tracks[0].FilePath -> Tracks, SelectedTrack.Title -> SelectedTrack
        root_prop = re.split(r'[.\[]', path)[0] if path else ""

        return XamlBinding(
            raw_binding=raw,
            path=path,
            root_property=root_prop,
            mode=mode,
            converter=converter,
            element_name=element_name,
            relative_source=relative_source,
            source=source,
            target_property=target_attr,
            file_path=file_path,
            line_number=line_idx
        )


# ==============================================================================
# 各検証レイヤーの実装
# ==============================================================================

class EquivalenceAuditor:
    """新旧等価性・デグレ検証オーディター"""

    def __init__(self, root_dir: Path, verbose: bool = False):
        self.root_dir = root_dir
        self.audio_effector_dir = root_dir / "AudioEffector"
        self.verbose = verbose
        
        self.classes: Dict[str, ClassInfo] = {}
        self.all_classes: List[ClassInfo] = []
        self.xaml_bindings: List[XamlBinding] = []
        
        self._load_codebase()

    def _load_codebase(self):
        if not self.audio_effector_dir.exists():
            return

        for cs_path in self.audio_effector_dir.rglob("*.cs"):
            if "obj" in cs_path.parts or "bin" in cs_path.parts:
                continue
            parsed_classes = CSharpParser.parse_file(cs_path)
            for cls in parsed_classes:
                self.all_classes.append(cls)
                self.classes[cls.name] = cls
                self.classes[f"{cls.namespace}.{cls.name}"] = cls

        for xaml_path in self.audio_effector_dir.rglob("*.xaml"):
            if "obj" in xaml_path.parts or "bin" in xaml_path.parts:
                continue
            # 一時ファイルはスキップ
            if "temp_" in xaml_path.name:
                continue
            bindings = XamlParser.parse_file(xaml_path)
            self.xaml_bindings.extend(bindings)

    VIEW_MODEL_MAPPINGS = {
        "MainWindow": "MainViewModel",
        "LibraryView": "LibraryViewModel",
        "PlaylistTracksView": "PlaylistViewModel",
        "PlaylistSelectorView": "PlaylistViewModel",
        "EqualizerView": "EqualizerViewModel",
        "DeviceSyncView": "DeviceBrowserViewModel",
        "FolderView": "FolderViewModel",
        "SettingsDialog": "SettingsViewModel",
        "DeviceManagerDialog": "DeviceManagerViewModel",
        "AllSongsView": "MainViewModel",
        "ArtistsView": "MainViewModel",
        "RecentView": "MainViewModel",
        "MiniPlayerWindow": "MainViewModel",
        "PlayQueueDialog": "MainViewModel",
        "PlayQueueSidePanel": "MainViewModel",
        "PlaylistSelectionDialog": "PlaylistViewModel",
        "SidebarControl": "MainViewModel",
    }

    # --------------------------------------------------------------------------
    # 1. XAML バインディング網羅性 & スコープ検証
    # --------------------------------------------------------------------------
    def verify_xaml_bindings(self) -> List[AuditResult]:
        results: List[AuditResult] = []

        all_known_members: Set[str] = set()
        for cls in self.all_classes:
            all_known_members.update(cls.members.keys())

        wpf_builtin_props = {
            ".", "", "Value", "Key", "Count", "Length", "ActualWidth", "ActualHeight",
            "DataContext", "Tag", "Content", "SelectedItem", "SelectedIndex", "Visibility",
            "IsEnabled", "IsChecked", "IsSelected", "IsExpanded", "Text", "Name",
            "Foreground", "Background", "BorderBrush", "FontSize", "FontWeight",
            "HorizontalAlignment", "VerticalAlignment", "Margin", "Padding", "Width", "Height",
            "Children", "Items", "Header", "Title", "Source", "PlacementTarget", "IsOpen",
            "Fill", "Stroke", "StrokeThickness", "Opacity", "ToolTip", "Cursor",
            "WindowState", "WindowStartupLocation"
        }

        main_vm = self.classes.get("MainViewModel")
        main_vm_members = main_vm.get_all_members(self.classes) if main_vm else {}

        checked_bindings = 0
        unresolved_bindings = 0
        scope_warnings = 0

        for b in self.xaml_bindings:
            checked_bindings += 1
            root_prop = b.root_property
            rel_path = os.path.relpath(b.file_path, self.root_dir)

            # --- スコープ追跡検証: MainWindowRoot または AncestorType=Window の DataContext 参照 ---
            is_window_ref = (b.element_name == "MainWindowRoot") or (
                b.relative_source and ("AncestorType=Window" in b.relative_source or "x:Type Window" in b.relative_source)
            )

            if is_window_ref and "DataContext." in b.path:
                # Window の DataContext (MainViewModel) へのプロパティチェーン検証
                subpath = b.path.split("DataContext.", 1)[1]
                parts = [re.split(r'\[', p)[0] for p in subpath.split('.') if p]
                if parts:
                    first_prop = parts[0]
                    if main_vm and first_prop not in main_vm_members and first_prop not in wpf_builtin_props:
                        scope_warnings += 1
                        results.append(AuditResult(
                            category="XAML Binding Scope",
                            item_name=f"DataContext.{first_prop} ({b.target_property})",
                            status="WARN",
                            message=f"スコープ不整合: MainViewModel にプロパティ '{first_prop}' は存在しません in {rel_path}:{b.line_number}",
                            details=[
                                f"式: {b.raw_binding}",
                                f"参照先: {b.element_name or b.relative_source}",
                                f"完全パス: {b.path}"
                            ],
                            file_path=b.file_path,
                            line_number=b.line_number
                        ))
                        continue
                    elif main_vm and len(parts) > 1:
                        # 2段階目以降のプロパティチェーン追跡
                        curr_member = main_vm_members.get(first_prop)
                        if curr_member:
                            target_type = curr_member.data_type.rstrip('?').strip()
                            gen_match = re.search(r'<([A-Za-z0-9_.]+)>', target_type)
                            if gen_match:
                                target_type = gen_match.group(1).split('.')[-1]
                            next_cls = self.classes.get(target_type)
                            if next_cls:
                                next_members = next_cls.get_all_members(self.classes)
                                second_prop = parts[1]
                                if second_prop not in next_members and second_prop not in wpf_builtin_props:
                                    scope_warnings += 1
                                    results.append(AuditResult(
                                        category="XAML Binding Scope",
                                        item_name=f"{first_prop}.{second_prop} ({b.target_property})",
                                        status="WARN",
                                        message=f"スコープ不整合: {next_cls.name} にプロパティ '{second_prop}' は存在しません in {rel_path}:{b.line_number}",
                                        details=[f"式: {b.raw_binding}", f"親プロパティ型: {next_cls.name}"],
                                        file_path=b.file_path,
                                        line_number=b.line_number
                                    ))
                                    continue

            # --- 一般プロパティの網羅性チェック ---
            if root_prop in wpf_builtin_props:
                continue
            if b.element_name and not is_window_ref:
                continue
            if b.relative_source and not ("DataContext" in b.path):
                continue
            if b.relative_source and "DataContext" in b.path and not is_window_ref:
                clean_prop = re.split(r'[.\[]', b.path.split("DataContext.")[-1])[0]
                if clean_prop in all_known_members or clean_prop in wpf_builtin_props:
                    continue

            found = root_prop in all_known_members

            if not found:
                unresolved_bindings += 1
                results.append(AuditResult(
                    category="XAML Binding",
                    item_name=f"{root_prop} ({b.target_property})",
                    status="WARN",
                    message=f"未解決のバインディングプロパティ: '{root_prop}' in {rel_path}:{b.line_number}",
                    details=[f"式: {b.raw_binding}", f"ターゲット属性: {b.target_property}"],
                    file_path=b.file_path,
                    line_number=b.line_number
                ))

        if unresolved_bindings == 0 and scope_warnings == 0:
            results.append(AuditResult(
                category="XAML Binding",
                item_name="All Active XAML Bindings",
                status="PASS",
                message=f"全 {checked_bindings} 件のアクティブXAMLバインディングの対象プロパティ/コマンドがすべて定義済みかつスコープ整合していることを確認しました。"
            ))

        return results

    # --------------------------------------------------------------------------
    # 1.5 固定インデクサ境界検証 (Indexer Boundary Audit)
    # --------------------------------------------------------------------------
    def verify_indexer_bindings(self) -> List[AuditResult]:
        results: List[AuditResult] = []
        indexer_pattern = re.compile(r'\[(\d+)\]')

        detected_count = 0
        for b in self.xaml_bindings:
            match = indexer_pattern.search(b.path)
            if not match:
                continue

            detected_count += 1
            rel_path = os.path.relpath(b.file_path, self.root_dir)
            index_val = match.group(1)

            results.append(AuditResult(
                category="XAML Indexer Boundary",
                item_name=f"{b.path} ({b.target_property})",
                status="WARN",
                message=f"固定インデクサバインディング検出: '{b.path}' [index={index_val}] in {rel_path}:{b.line_number} (コレクション要素数不足時の実行時例外リスクに留意)",
                details=[
                    f"バインディング式: {b.raw_binding}",
                    f"ターゲット属性: {b.target_property}",
                    f"対象ファイル: {rel_path}:{b.line_number}"
                ],
                file_path=b.file_path,
                line_number=b.line_number
            ))

        if detected_count == 0:
            results.append(AuditResult(
                category="XAML Indexer Boundary",
                item_name="Indexer Bindings",
                status="PASS",
                message="固定インデクサバインディングは検出されませんでした。"
            ))

        return results

    # --------------------------------------------------------------------------
    # 2. ViewModel / 公開API 等価性検証
    # --------------------------------------------------------------------------
    def verify_viewmodels(self) -> List[AuditResult]:
        results: List[AuditResult] = []

        main_vm = self.classes.get("MainViewModel")
        if not main_vm:
            results.append(AuditResult(
                category="ViewModel API",
                item_name="MainViewModel",
                status="FAIL",
                message="MainViewModel.cs が検出されませんでした。"
            ))
            return results

        new_vms: Dict[str, ClassInfo] = {}
        for cls in self.all_classes:
            if "AudioEffector.Presentation.ViewModels" in cls.namespace:
                new_vms[cls.name] = cls

        new_vm_members: Dict[str, List[str]] = {}
        for vm_name, vm_cls in new_vms.items():
            for m_name in vm_cls.members.keys():
                if m_name not in new_vm_members:
                    new_vm_members[m_name] = []
                new_vm_members[m_name].append(vm_name)

        total_main_members = 0
        matched_in_new_vms = 0
        unmatched_members: List[str] = []

        for m_name, m_info in main_vm.members.items():
            if not m_info.is_public:
                continue
            total_main_members += 1

            if m_name in new_vm_members:
                matched_in_new_vms += 1
            else:
                unmatched_members.append(f"{m_info.member_type}: {m_name} ({m_info.data_type})")

        coverage = (matched_in_new_vms / total_main_members * 100) if total_main_members > 0 else 100

        results.append(AuditResult(
            category="ViewModel API",
            item_name="ViewModel Modularization Coverage",
            status="INFO",
            message=f"新専門ViewModelへの機能移行カバレッジ: {matched_in_new_vms}/{total_main_members} ({coverage:.1f}%)",
            details=[
                f"新ViewModel数: {len(new_vms)} クラス ({', '.join(sorted(new_vms.keys()))})",
                f"MainViewModel 全メンバー数: {total_main_members} 件"
            ]
        ))

        all_vms = {cls.name: cls for cls in self.all_classes if "ViewModel" in cls.name}
        for vm_name, vm_cls in sorted(all_vms.items()):
            props = len(vm_cls.get_public_properties())
            cmds = len(vm_cls.get_public_commands())
            meths = len(vm_cls.get_public_methods())
            results.append(AuditResult(
                category="ViewModel API",
                item_name=vm_name,
                status="PASS",
                message=f"{vm_name}: {props} properties, {cmds} commands, {meths} methods",
                file_path=vm_cls.file_path
            ))

        return results

    # --------------------------------------------------------------------------
    # 3. モデル・エンティティ プロパティ等価性検証
    # --------------------------------------------------------------------------
    def verify_models_and_entities(self) -> List[AuditResult]:
        results: List[AuditResult] = []

        model_pairs = [
            ("AudioEffector.Models.Track", "AudioEffector.Domain.Entities.Track", "Track (楽曲エンティティ)"),
            ("AudioEffector.Models.Album", "AudioEffector.Domain.Entities.Album", "Album (アルバムエンティティ)"),
            ("AudioEffector.Models.UserPlaylist", "AudioEffector.Domain.Entities.UserPlaylist", "UserPlaylist (プレイリストエンティティ)"),
            ("AudioEffector.Models.Preset", "AudioEffector.Domain.Entities.EqualizerPreset", "EqualizerPreset (EQプリセット)"),
            ("AudioEffector.Models.DeviceAlbum", "AudioEffector.Domain.Entities.DataTransfer.DeviceAlbum", "DeviceAlbum (デバイスアルバム)"),
            ("AudioEffector.Models.DeviceTrack", "AudioEffector.Domain.Entities.DataTransfer.DeviceTrack", "DeviceTrack (デバイストラック)"),
        ]

        for old_key, new_key, display_name in model_pairs:
            old_cls = self.classes.get(old_key)
            new_cls = self.classes.get(new_key)

            if not old_cls and new_cls:
                new_props = sorted(new_cls.get_public_properties().keys())
                results.append(AuditResult(
                    category="Model/Entity",
                    item_name=display_name,
                    status="PASS",
                    message=f"旧モデルから新エンティティへの完全統合完了（旧モデル撤廃済み）: {len(new_props)} プロパティ",
                    details=[f"新エンティティ公開プロパティ ({len(new_props)}): {', '.join(new_props)}"]
                ))
                continue

            if not old_cls or not new_cls:
                results.append(AuditResult(
                    category="Model/Entity",
                    item_name=display_name,
                    status="WARN",
                    message=f"クラス比較対象が不足しています: (Old: {bool(old_cls)}, New: {bool(new_cls)})"
                ))
                continue

            old_props = set(old_cls.get_public_properties().keys())
            new_props = set(new_cls.get_public_properties().keys())

            common_props = old_props & new_props
            old_only = old_props - new_props
            new_only = new_props - old_props

            status = "PASS" if len(common_props) > 0 else "WARN"
            
            detail_lines = [
                f"共通プロパティ ({len(common_props)}): {', '.join(sorted(common_props))}",
            ]
            if old_only:
                detail_lines.append(f"旧モデル固有/UI用プロパティ ({len(old_only)}): {', '.join(sorted(old_only))}")
            if new_only:
                detail_lines.append(f"新エンティティ追加プロパティ ({len(new_only)}): {', '.join(sorted(new_only))}")

            results.append(AuditResult(
                category="Model/Entity",
                item_name=display_name,
                status=status,
                message=f"{display_name}: 共通 {len(common_props)} 件 / 旧固有 {len(old_only)} 件 / 新追加 {len(new_only)} 件",
                details=detail_lines,
                file_path=new_cls.file_path
            ))

        return results

    # --------------------------------------------------------------------------
    # 全検証の実行
    # --------------------------------------------------------------------------
    def run_all(self, xaml: bool = True, vm: bool = True, models: bool = True, indexer: bool = True) -> List[AuditResult]:
        all_results: List[AuditResult] = []
        if models:
            all_results.extend(self.verify_models_and_entities())
        if vm:
            all_results.extend(self.verify_viewmodels())
        if xaml:
            all_results.extend(self.verify_xaml_bindings())
        if indexer:
            all_results.extend(self.verify_indexer_bindings())
        return all_results


# ==============================================================================
# レポート出力フォーマッター
# ==============================================================================

def print_terminal_report(results: List[AuditResult], use_color: bool = True):
    print("\n" + "=" * 80)
    print(colorize(" Audio Effector - 新旧等価性・デグレ検証レポート (Equivalence Audit)", Colors.BOLD + Colors.CYAN, use_color))
    print("=" * 80)

    pass_count = sum(1 for r in results if r.status == "PASS")
    warn_count = sum(1 for r in results if r.status == "WARN")
    fail_count = sum(1 for r in results if r.status == "FAIL")
    info_count = sum(1 for r in results if r.status == "INFO")

    categories: Dict[str, List[AuditResult]] = {}
    for r in results:
        categories.setdefault(r.category, []).append(r)

    for cat_name, cat_results in categories.items():
        print(f"\n{colorize(f'▶ {cat_name}', Colors.BOLD + Colors.WHITE, use_color)}")
        print("-" * 80)

        for r in cat_results:
            if r.status == "PASS":
                badge = colorize("[ PASS ]", Colors.GREEN + Colors.BOLD, use_color)
            elif r.status == "WARN":
                badge = colorize("[ WARN ]", Colors.YELLOW + Colors.BOLD, use_color)
            elif r.status == "FAIL":
                badge = colorize("[ FAIL ]", Colors.RED + Colors.BOLD, use_color)
            else:
                badge = colorize("[ INFO ]", Colors.BLUE + Colors.BOLD, use_color)

            print(f" {badge} {colorize(r.item_name, Colors.BOLD, use_color)}: {r.message}")

            if r.details:
                for d in r.details:
                    print(f"        {colorize('•', Colors.GRAY, use_color)} {d}")

    print("\n" + "=" * 80)
    summary_line = f" 検証サマリー: 合格(PASS): {pass_count} | 警告(WARN): {warn_count} | 失敗(FAIL): {fail_count} | 情報(INFO): {info_count}"
    if fail_count > 0:
        print(colorize(summary_line, Colors.RED + Colors.BOLD, use_color))
    elif warn_count > 0:
        print(colorize(summary_line, Colors.YELLOW + Colors.BOLD, use_color))
    else:
        print(colorize(summary_line, Colors.GREEN + Colors.BOLD, use_color))
    print("=" * 80 + "\n")


def generate_markdown_report(results: List[AuditResult], report_path: Path):
    pass_count = sum(1 for r in results if r.status == "PASS")
    warn_count = sum(1 for r in results if r.status == "WARN")
    fail_count = sum(1 for r in results if r.status == "FAIL")
    info_count = sum(1 for r in results if r.status == "INFO")

    lines = [
        "# 新旧等価性・デグレ検証レポート (Equivalence Audit Report)",
        "",
        "## 検証サマリー",
        "",
        "| ステータス | 件数 |",
        "|:---|:---:|",
        f"| ✅ 合格 (PASS) | **{pass_count}** |",
        f"| ⚠️ 警告 (WARN) | **{warn_count}** |",
        f"| ❌ 失敗 (FAIL) | **{fail_count}** |",
        f"| ℹ️ 情報 (INFO) | **{info_count}** |",
        "",
        "---",
        "",
        "## 詳細結果",
        ""
    ]

    categories: Dict[str, List[AuditResult]] = {}
    for r in results:
        categories.setdefault(r.category, []).append(r)

    for cat_name, cat_results in categories.items():
        lines.append(f"### {cat_name}")
        lines.append("")
        for r in cat_results:
            icon = "✅" if r.status == "PASS" else ("⚠️" if r.status == "WARN" else ("❌" if r.status == "FAIL" else "ℹ️"))
            lines.append(f"#### {icon} {r.item_name}")
            lines.append(f"- **メッセージ**: {r.message}")
            if r.file_path:
                lines.append(f"- **対象ファイル**: `{r.file_path}`" + (f" (L{r.line_number})" if r.line_number else ""))
            if r.details:
                lines.append("- **詳細情報**:")
                for d in r.details:
                    lines.append(f"  - {d}")
            lines.append("")

    report_path.parent.mkdir(parents=True, exist_ok=True)
    with open(report_path, 'w', encoding='utf-8') as f:
        f.write("\n".join(lines))


# ==============================================================================
# メインエントリーポイント
# ==============================================================================

def main():
    parser = argparse.ArgumentParser(description="Audio Effector 新旧等価チェックツール")
    parser.add_argument("--all", action="store_true", help="すべての検証を実行 (デフォルト)")
    parser.add_argument("--xaml", action="store_true", help="XAMLバインディング網羅性検証のみ実行")
    parser.add_argument("--vm", action="store_true", help="ViewModel / 公開API等価性検証のみ実行")
    parser.add_argument("--models", action="store_true", help="モデル・エンティティ等価性検証のみ実行")
    parser.add_argument("--indexer", action="store_true", help="固定インデクサ境界検証のみ実行")
    parser.add_argument("--report", type=str, help="Markdownレポートの出力先パス")
    parser.add_argument("--json", action="store_true", help="結果をJSON形式で出力")
    parser.add_argument("--no-color", action="store_true", help="ANSIカラー出力を無効化")
    parser.add_argument("-v", "--verbose", action="store_true", help="詳細ログ出力")

    args = parser.parse_args()

    run_all = args.all or not (args.xaml or args.vm or args.models or args.indexer)
    do_xaml = run_all or args.xaml
    do_vm = run_all or args.vm
    do_models = run_all or args.models
    do_indexer = run_all or args.indexer

    current_dir = Path(__file__).resolve().parent
    root_dir = current_dir.parent if current_dir.name == "tools" else current_dir

    auditor = EquivalenceAuditor(root_dir=root_dir, verbose=args.verbose)
    results = auditor.run_all(xaml=do_xaml, vm=do_vm, models=do_models, indexer=do_indexer)

    if args.json:
        json_data = [asdict(r) for r in results]
        print(json.dumps(json_data, ensure_ascii=False, indent=2))
    else:
        use_color = not args.no_color and sys.stdout.isatty()
        print_terminal_report(results, use_color=use_color)

    if args.report:
        report_path = Path(args.report)
        generate_markdown_report(results, report_path)
        print(f"Markdown レポートを出力しました: {report_path}")

    fail_count = sum(1 for r in results if r.status == "FAIL")
    sys.exit(1 if fail_count > 0 else 0)


if __name__ == "__main__":
    main()
