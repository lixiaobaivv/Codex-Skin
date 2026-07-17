fn main() {
    let args: Vec<_> = std::env::args().skip(1).collect();
    if args.len() != 2 {
        eprintln!("Usage: dreamskin-verify <package.dreamskin> <windows|macos>");
        std::process::exit(2);
    }
    if let Err(error) = codex_skin_lib::verify_dreamskin(&args[0], &args[1]) {
        eprintln!("{error}");
        std::process::exit(1);
    }
    println!("verified\t{}\t{}", args[1], args[0]);
}
