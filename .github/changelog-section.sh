#!/usr/bin/env bash
# Печатает раздел CHANGELOG.md для указанной версии.
# Если передана предыдущая выпущенная версия (3-й аргумент, или 2-й без пути к файлу),
# агрегирует все разделы от VERSION вниз до PREV_VERSION (не включая PREV_VERSION),
# объединяя одноимённые подзаголовки «### ...» в порядке первого появления.
# Нет раздела «## VERSION» или он пуст – ненулевой код возврата.
set -euo pipefail

VERSION="${1:?версия не передана}"
PREV_VERSION="${2:-}"
FILE="${3:-CHANGELOG.md}"

# Обратная совместимость: если 2-й аргумент – путь к файлу, а не версия.
if [ -n "$PREV_VERSION" ] && [ -f "$PREV_VERSION" ] && [ $# -eq 2 ]; then
  FILE="$PREV_VERSION"
  PREV_VERSION=""
fi

PREV_VERSION="${PREV_VERSION#v}"
PREV_VERSION="${PREV_VERSION#V}"

section=$(awk -v target="## $VERSION" -v prev="$PREV_VERSION" '
  function trim(s) {
    sub(/^[[:space:]]+/, "", s)
    sub(/[[:space:]]+$/, "", s)
    return s
  }
  function add_line(group, line) {
    if (!(group in seen_group)) {
      seen_group[group] = 1
      group_order[++group_count] = group
    }
    count = ++group_len[group]
    group_lines[group, count] = line
  }
  {
    sub(/\r$/, "")
    if ($0 == target) {
      in_range = 1
      found_target = 1
      current_group = ""
      next
    }
    if (in_range && /^## /) {
      if (prev == "") {
        exit
      }
      if ($0 == "## " prev) {
        exit
      }
      current_group = ""
      next
    }
    if (!in_range) {
      next
    }
    if (/^### /) {
      current_group = trim(substr($0, 5))
      next
    }
    if (trim($0) == "") {
      next
    }
    add_line(current_group, $0)
  }
  END {
    if (!found_target || group_count == 0) {
      exit 1
    }
    printed = 0
    for (g = 1; g <= group_count; g++) {
      name = group_order[g]
      if (printed) {
        print ""
      }
      if (name != "") {
        print "### " name
        print ""
      }
      for (i = 1; i <= group_len[name]; i++) {
        print group_lines[name, i]
      }
      printed = 1
    }
  }
' "$FILE" || true)

if [ -z "$(printf '%s' "$section" | tr -d '[:space:]')" ]; then
  echo "CHANGELOG.md: нет раздела «## $VERSION» или он пуст" >&2
  exit 1
fi

printf '%s\n' "$section"
