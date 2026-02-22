# Makefile для генерации Swift кода из .proto файлов
#
# Использование:
#   make generate    - сгенерировать Swift код
#   make clean       - удалить сгенерированные файлы

PROTO_DIR = ../../Protos
OUT_DIR = Sources/BFProto/Generated

# Все proto файлы
PROTO_FILES = $(wildcard $(PROTO_DIR)/*.proto)

.PHONY: generate clean

generate: $(OUT_DIR)
	@echo "Генерация Swift кода из proto файлов..."
	protoc \
		--proto_path=$(PROTO_DIR) \
		--swift_out=$(OUT_DIR) \
		--swift_opt=Visibility=Public \
		--grpc-swift_out=$(OUT_DIR) \
		--grpc-swift_opt=Visibility=Public,Client=true,Server=false \
		$(PROTO_FILES)
	@echo "Генерация завершена. Файлы в $(OUT_DIR)"

$(OUT_DIR):
	mkdir -p $(OUT_DIR)

clean:
	rm -rf $(OUT_DIR)/*.swift
	@echo "Сгенерированные файлы удалены"
