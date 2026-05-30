// Тонкие обёртки над MessagesApi (unary). Авто-token/refresh — через интерсептор транспорта.
import { messagesClient } from '../transport';
import {
  DeleteMessageRequest,
  EditMessageRequest,
  ListChatsRequest,
  ListMessagesRequest,
  ListPinnedMessagesRequest,
  MarkAsReadRequest,
  OutgoingMessage,
  PinMessageRequest,
  SendMessageRequest,
  UnpinAllRequest,
  UnpinMessageRequest,
} from '../../gen/messages_api_pb';
import { PageRequest } from '../../gen/shared_pb';

export async function listChats(offset = 0, size = 50) {
  const resp = await messagesClient.listChats(
    new ListChatsRequest({ pagination: new PageRequest({ offset, size }) }),
  );
  return { chats: resp.chats, totalCount: resp.totalCount };
}

export interface ListMessagesOpts {
  fromMessageId?: bigint;
  offsetBefore?: number; // более старые
  offsetAfter?: number; // более новые
}

export async function listMessages(chatId: string, opts: ListMessagesOpts = {}) {
  const resp = await messagesClient.listMessages(
    new ListMessagesRequest({
      chatId,
      fromMessageId: opts.fromMessageId ?? 0n,
      offsetBefore: opts.offsetBefore ?? 0,
      offsetAfter: opts.offsetAfter ?? 0,
    }),
  );
  return resp.messages;
}

export async function sendMessage(chatId: string, text: string, filesIds: string[] = []) {
  const resp = await messagesClient.sendMessage(
    new SendMessageRequest({
      sourceId: { case: 'chatId', value: chatId },
      message: new OutgoingMessage({ text, filesIds }),
    }),
  );
  return resp.message;
}

export async function editMessage(messageId: bigint, text: string, filesIds: string[] = []) {
  const resp = await messagesClient.editMessage(new EditMessageRequest({ messageId, text, filesIds }));
  return resp.message;
}

export async function deleteMessage(messageId: bigint) {
  await messagesClient.deleteMessage(new DeleteMessageRequest({ messageId }));
}

export async function markAsRead(messageIds: bigint[]) {
  if (messageIds.length === 0) return;
  await messagesClient.markAsRead(new MarkAsReadRequest({ messageIds }));
}

export async function pinMessage(chatId: string, messageId: bigint) {
  const resp = await messagesClient.pinMessage(new PinMessageRequest({ chatId, messageId }));
  return resp.pinned;
}

export async function unpinMessage(chatId: string, messageId: bigint) {
  await messagesClient.unpinMessage(new UnpinMessageRequest({ chatId, messageId }));
}

export async function unpinAll(chatId: string) {
  await messagesClient.unpinAll(new UnpinAllRequest({ chatId }));
}

export async function listPinnedMessages(chatId: string, offset = 0, size = 50) {
  const resp = await messagesClient.listPinnedMessages(
    new ListPinnedMessagesRequest({ chatId, pagination: new PageRequest({ offset, size }) }),
  );
  return resp.pinned;
}
