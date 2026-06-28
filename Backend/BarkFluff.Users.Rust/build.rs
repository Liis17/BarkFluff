use std::path::PathBuf;

fn main() {
    // protoc без системной зависимости — берём бинарь из vendored-крейта.
    std::env::set_var(
        "PROTOC",
        protoc_bin_vendored::protoc_bin_path().expect("vendored protoc"),
    );

    let proto_root = PathBuf::from("../../Shared/BarkFluff.Proto");
    let local_proto = PathBuf::from("proto"); // вендорённый google/protobuf/timestamp.proto
    let out_dir = PathBuf::from(std::env::var("OUT_DIR").expect("OUT_DIR"));

    let protos = [
        proto_root.join("shared.proto"),
        proto_root.join("users_api.proto"),
        proto_root.join("configuration_api.proto"),
        proto_root.join("files_api.proto"),
        proto_root.join("messages_api.proto"),
    ];

    tonic_build::configure()
        .build_server(true)
        .build_client(true)
        .file_descriptor_set_path(out_dir.join("users_descriptor.bin"))
        // google.protobuf.Timestamp → prost_types, собственный тип не генерируем.
        .extern_path(".google.protobuf.Timestamp", "::prost_types::Timestamp")
        .compile_protos(&protos, &[proto_root.clone(), local_proto])
        .expect("failed to compile protos");

    for p in &protos {
        println!("cargo:rerun-if-changed={}", p.display());
    }
    println!("cargo:rerun-if-changed=proto/google/protobuf/timestamp.proto");
}
