# job-probe

Image tối thiểu để kiểm chứng Job của Shipit. Args là một chuỗi lệnh shell, ví dụ:

- `echo hello; exit 0` -> run `success`
- `echo fail; exit 3` -> run `failed`, exit code 3
