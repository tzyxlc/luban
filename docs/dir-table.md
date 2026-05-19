# 目录表 dirTable

`dirTable` 用于把一个目录下结构相同的多个数据表合并成一张 map 表。每个文件会生成一条外层记录，文件名去掉扩展名后作为 key，文件内的数据行会写入外层记录的列表、数组或 map 字段。

例如目录结构：

```text
Datas/cfg/level/1001.xlsx
Datas/cfg/level/1002.xlsx
Datas/cfg/level/1003.xlsx
```

可以生成一张 `cfg.Tblevels` 表，并通过 `cfg.Tblevels[1001]` 或对应语言的 `Get(1001)` 获取 `1001.xlsx` 对应的整份配置。

## 配置方式

在 `__tables__.xlsx` 中增加或修改一行：

| full_name | value_type | read_schema_from_file | input | index | mode | tags |
| --- | --- | --- | --- | --- | --- | --- |
| cfg.Tblevels | cfg.levelfile | false | cfg/level | level_id | map | dirTable#fileKeyType=int32#fileKeyField=level_id#fileValueField=nodes#fileValueKeyField=id |

字段含义：

- `full_name`：最终生成的表名。
- `value_type`：外层记录类型，不能直接填目录内单个文件的行类型。
- `read_schema_from_file`：通常设为 `false`，外层记录类型需要在 bean 定义中显式声明。
- `input`：目录路径，相对于 `dataDir`。
- `index`：map 表的 key 字段，必须与 `fileKeyField` 一致。
- `mode`：必须为 `map`。
- `tags`：启用 `dirTable` 并指定文件名 key 和文件内容字段。

`tags` 支持以下属性：

- `dirTable`：启用目录表加载。
- `fileKeyType`：文件名 key 的类型。
- `fileKeyField`：外层记录中接收文件名 key 的字段。
- `fileValueField`：外层记录中接收文件内数据行的列表、数组或 map 字段。
- `fileValueKeyField`：当 `fileValueField` 是 map 时必填，表示用行记录中的哪个字段作为 map key。

## Bean 定义

假设每个 `cfg/level/*.xlsx` 文件里的行结构是 `cfg.levelnode`，还需要额外定义一个外层记录类型：

| full_name | fields.name | fields.type | comment |
| --- | --- | --- | --- |
| cfg.levelfile | level_id | int | 关卡 ID |
|  | nodes | map,int,cfg.levelnode | 当前文件的所有节点 |

`cfg.levelnode` 仍然是目录内单个 xlsx 的行结构。

外层类型是必需的，因为 `dirTable` 生成的是“文件级配置”：

```text
cfg.Tblevels[1001] -> cfg.levelfile
cfg.levelfile.nodes -> 1001.xlsx 内的所有 cfg.levelnode 行
```

如果把 `value_type` 直接写成 `cfg.levelnode`，生成会失败，因为 `cfg.levelnode` 没有 `fileValueField` 指定的 `nodes` 字段，也无法承载一个文件内的多行数据。

## 支持的 key 类型

`fileKeyType` 目前支持 Luban 已有的基础类型：

- `byte` 或 `uint8`
- `short` 或 `int16`
- `int` 或 `int32`
- `long`、`int64` 或 `bigint`
- `string`

整型 key 会按文件名解析。例如 `1001.xlsx` 在 `fileKeyType=int32` 时解析为 `1001`。`string` key 会直接使用文件名字符串。

## 生成后访问

Go 示例：

```go
level := tables.Tblevels.Get(1001)
nodes := level.Nodes
```

C#、C++ 等语言会按各自模板生成等价的 map 访问接口。
