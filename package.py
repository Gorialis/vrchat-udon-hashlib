#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
MIT License

Copyright (c) 2021-present Devon (Gorialis) R

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
"""

import io
import pathlib
import subprocess
import tarfile
from dataclasses import dataclass

import yaml


PROJECT_DIRECTORY = pathlib.Path(__file__).parent
BUILDS_DIRECTORY = PROJECT_DIRECTORY / 'Builds'
ASSETS_DIRECTORY = PROJECT_DIRECTORY / 'Assets'
GORIALIS_DIRECTORY = ASSETS_DIRECTORY / 'Gorialis'


@dataclass
class UnityPackageItem:
    path: str
    guid: str
    meta_content: bytes
    content: bytes | None = None

    @classmethod
    def from_path(cls, path: pathlib.Path) -> 'UnityPackageItem':
        meta_path = path.with_name(path.name + '.meta')

        with open(meta_path, 'rb') as fp:
            meta_content = fp.read()
            guid = yaml.safe_load(meta_content)['guid']

        if path.is_dir():
            content = None
        else:
            with open(path, 'rb') as fp:
                content = fp.read()

        return cls(
            path=path.relative_to(PROJECT_DIRECTORY).as_posix(),
            guid=guid,
            meta_content=meta_content,
            content=content
        )

    @property
    def data(self) -> dict[str, bytes]:
        value = {
            "asset.meta": self.meta_content,
            "pathname": self.path.encode('utf-8')
        }

        if self.content:
            value['asset'] = self.content

        return value

    def add_to(self, package: tarfile.TarFile):
        for filename, content in self.data.items():
            info = tarfile.TarInfo(name=f"{self.guid}/{filename}")
            info.size = len(content)

            package.addfile(info, io.BytesIO(content))

    @property
    def is_directory(self) -> bool:
        return self.content is None

    @property
    def is_file(self) -> bool:
        return self.content is not None


PROCESS = subprocess.Popen(
    ['git', 'rev-parse', '--short', 'HEAD'],
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE
)

COMMIT_HASH, ERR = PROCESS.communicate()

BUILDS_DIRECTORY.mkdir(exist_ok=True)

for project in GORIALIS_DIRECTORY.iterdir():
    if not project.is_dir():
        continue

    print(f"Building {project.name}")

    with open(project / 'version.txt', 'r', encoding='utf-8') as fp:
        version = fp.read().strip()

    with open(BUILDS_DIRECTORY / f'{project.name}.{version}.unitypackage', 'wb') as outer_package:
        with tarfile.open(name='archtemp.tar', fileobj=outer_package, mode='w:gz') as package:
            for file in project.glob("**/*"):
                if file.name.endswith('.meta'):
                    continue

                if file.name == '.icon.png':
                    continue

                item = UnityPackageItem.from_path(file)

                print(f"  Adding {file.relative_to(PROJECT_DIRECTORY).as_posix()}")

                if file == project / 'version.txt':
                    if COMMIT_HASH:
                        item.content = version.encode('utf-8') + b'\n' + COMMIT_HASH
                    else:
                        item.content = version.encode('utf-8')

                item.add_to(package)

            with open(project / '.icon.png', 'rb') as fp:
                info = package.gettarinfo(arcname='.icon.png', fileobj=fp)
                package.addfile(info, fp)
