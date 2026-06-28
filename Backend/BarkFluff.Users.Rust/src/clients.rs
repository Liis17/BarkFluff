//! gRPC-клиенты к Files/Messages с JWT-интерцептором (service-токен в `x-auth-token`).
//! Эквивалент AddGrpcClient + JwtClientInterceptor.

use crate::proto::barkfluff::files::files_server_api_client::FilesServerApiClient;
use crate::proto::barkfluff::files::{
    GetFileDataRequest, GetFileDataResponse, GetFilesDataRequest, GetFilesDataResponse,
};
use crate::proto::barkfluff::messages::messages_server_api_client::MessagesServerApiClient;
use crate::proto::barkfluff::messages::{GetUserAllMessagesRequest, GetUserAllMessagesResponse};
use tonic::metadata::MetadataValue;
use tonic::service::interceptor::InterceptedService;
use tonic::transport::Channel;
use tonic::{Request, Status};

#[derive(Clone)]
pub struct ServiceClients {
    files_channel: Channel,
    files_token: String,
    messages_channel: Channel,
    messages_token: String,
}

fn auth_interceptor(token: String) -> impl FnMut(Request<()>) -> Result<Request<()>, Status> + Clone {
    move |mut req: Request<()>| {
        if let Ok(val) = MetadataValue::try_from(token.as_str()) {
            req.metadata_mut().insert("x-auth-token", val);
        }
        Ok(req)
    }
}

impl ServiceClients {
    /// Ленивое подключение — не падает на старте, если сервисы ещё не подняты.
    pub fn new(
        files_host: String,
        files_token: String,
        messages_host: String,
        messages_token: String,
    ) -> anyhow::Result<Self> {
        let files_channel = Channel::from_shared(files_host)?.connect_lazy();
        let messages_channel = Channel::from_shared(messages_host)?.connect_lazy();
        Ok(Self {
            files_channel,
            files_token,
            messages_channel,
            messages_token,
        })
    }

    fn files(
        &self,
    ) -> FilesServerApiClient<InterceptedService<Channel, impl FnMut(Request<()>) -> Result<Request<()>, Status> + Clone>>
    {
        FilesServerApiClient::with_interceptor(
            self.files_channel.clone(),
            auth_interceptor(self.files_token.clone()),
        )
    }

    fn messages(
        &self,
    ) -> MessagesServerApiClient<InterceptedService<Channel, impl FnMut(Request<()>) -> Result<Request<()>, Status> + Clone>>
    {
        MessagesServerApiClient::with_interceptor(
            self.messages_channel.clone(),
            auth_interceptor(self.messages_token.clone()),
        )
    }

    pub async fn get_file_data(&self, file_id: &str) -> Result<GetFileDataResponse, Status> {
        Ok(self
            .files()
            .get_file_data(GetFileDataRequest {
                file_id: file_id.to_string(),
            })
            .await?
            .into_inner())
    }

    pub async fn get_files_data(&self, file_ids: Vec<String>) -> Result<GetFilesDataResponse, Status> {
        Ok(self
            .files()
            .get_files_data(GetFilesDataRequest { file_ids })
            .await?
            .into_inner())
    }

    pub async fn get_user_all_messages(
        &self,
        user_id: i64,
    ) -> Result<GetUserAllMessagesResponse, Status> {
        Ok(self
            .messages()
            .get_user_all_messages(GetUserAllMessagesRequest { user_id })
            .await?
            .into_inner())
    }
}
