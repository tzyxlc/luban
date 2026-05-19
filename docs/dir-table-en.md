# Directory-Backed Tables

`dirTable` loads multiple files with the same row schema from one directory into a single map table. Each file becomes one outer record. The file name without extension is used as the key, and the rows inside that file are written to a list or array field on the outer record.

Example directory:

```text
Datas/cfg/level/1001.xlsx
Datas/cfg/level/1002.xlsx
Datas/cfg/level/1003.xlsx
```

This can generate one `cfg.Tblevels` table, where `cfg.Tblevels[1001]` or the generated `Get(1001)` API returns the configuration loaded from `1001.xlsx`.

## Table Configuration

Add or update a row in `__tables__.xlsx`:

| full_name | value_type | read_schema_from_file | input | index | mode | tags |
| --- | --- | --- | --- | --- | --- | --- |
| cfg.Tblevels | cfg.levelfile | false | cfg/level | level_id | map | dirTable#fileKeyType=int32#fileKeyField=level_id#fileValueField=nodes |

Field meanings:

- `full_name`: generated table name.
- `value_type`: outer record type. Do not use the row type of files in the directory here.
- `read_schema_from_file`: usually `false`; define the outer record type explicitly as a bean.
- `input`: directory path, relative to `dataDir`.
- `index`: map key field. It must match `fileKeyField`.
- `mode`: must be `map`.
- `tags`: enables `dirTable` and configures the file-name key and file-content field.

Supported `tags` attributes:

- `dirTable`: enables directory-backed loading.
- `fileKeyType`: type used to parse the file-name key.
- `fileKeyField`: field on the outer record that receives the file-name key.
- `fileValueField`: list or array field on the outer record that receives rows loaded from each file.

## Bean Definition

Assume each `cfg/level/*.xlsx` file has row type `cfg.levelnode`. You also need an outer record type:

| full_name | fields.name | fields.type | comment |
| --- | --- | --- | --- |
| cfg.levelfile | level_id | int | level id |
|  | nodes | list,cfg.levelnode | all nodes from the current file |

`cfg.levelnode` remains the row type of each xlsx file in the directory.

The outer type is required because `dirTable` generates file-level records:

```text
cfg.Tblevels[1001] -> cfg.levelfile
cfg.levelfile.nodes -> all cfg.levelnode rows from 1001.xlsx
```

If `value_type` is set directly to `cfg.levelnode`, generation fails because `cfg.levelnode` does not contain the `nodes` field configured by `fileValueField`, and it cannot hold multiple rows from one file.

## Supported Key Types

`fileKeyType` currently supports existing Luban primitive types:

- `byte` or `uint8`
- `short` or `int16`
- `int` or `int32`
- `long`, `int64`, or `bigint`
- `string`

Integer keys are parsed from file names. For example, `1001.xlsx` becomes `1001` when `fileKeyType=int32`. `string` keys use the file name as-is.

## Generated Access

Go example:

```go
level := tables.Tblevels.Get(1001)
nodes := level.Nodes
```

C#, C++, and other targets generate equivalent map access APIs according to their templates.
