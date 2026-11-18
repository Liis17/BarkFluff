proxy_set_header X-BFT-Client-IP "";
proxy_set_header X-BFT-Proxy-Id $bft_proxy_id;
proxy_set_header X-BFT-Proxy-Secret "";
grpc_set_header X-BFT-Client-IP "";
grpc_set_header X-BFT-Proxy-Id $bft_proxy_id;
grpc_set_header X-BFT-Proxy-Secret "";
