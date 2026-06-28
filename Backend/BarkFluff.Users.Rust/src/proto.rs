//! Сгенерированные из .proto типы и gRPC-стабы.
//!
//! Модули названы по leaf-сегментам пакетов и лежат сиблингами под `barkfluff`,
//! чтобы кросс-пакетные ссылки prost (`super::shared::...`) резолвились.
#![allow(clippy::all)]
#![allow(dead_code)]
#![allow(rustdoc::broken_intra_doc_links)]

pub mod barkfluff {
    pub mod shared {
        tonic::include_proto!("barkfluff.shared");
    }
    pub mod users {
        tonic::include_proto!("barkfluff.users");
    }
    pub mod configuration {
        tonic::include_proto!("barkfluff.configuration");
    }
    pub mod files {
        tonic::include_proto!("barkfluff.files");
    }
    pub mod messages {
        tonic::include_proto!("barkfluff.messages");
    }
}

/// FileDescriptorSet для gRPC reflection (grpcurl и пр.).
pub const FILE_DESCRIPTOR_SET: &[u8] = tonic::include_file_descriptor_set!("users_descriptor");
