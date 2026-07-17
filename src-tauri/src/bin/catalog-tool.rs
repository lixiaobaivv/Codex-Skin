fn main() {
    let args: Vec<_> = std::env::args().skip(1).collect();
    let result = match args.first().map(String::as_str) {
        Some("index") if args.len() >= 2 && args.len() <= 3 => {
            codex_skin_lib::catalog_index(&args[1], args.get(2).map(String::as_str))
                .map(|count| format!("indexed\t{count}"))
        }
        Some("validate") if args.len() == 2 => {
            codex_skin_lib::catalog_validate(&args[1]).map(|count| format!("valid\t{count}"))
        }
        Some("pack") if args.len() == 3 => {
            codex_skin_lib::catalog_pack(&args[1], &args[2]).map(|count| format!("packed\t{count}"))
        }
        _ => {
            eprintln!("Usage: catalog-tool <index ROOT [NAME]|validate ROOT|pack ROOT OUTPUT>");
            std::process::exit(2)
        }
    };
    match result {
        Ok(message) => println!("{message}"),
        Err(error) => {
            eprintln!("{error}");
            std::process::exit(1)
        }
    }
}
