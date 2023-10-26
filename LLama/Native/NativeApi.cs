﻿using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using LLama.Exceptions;

#pragma warning disable IDE1006 // Naming Styles

namespace LLama.Native
{
    using llama_token = Int32;

    /// <summary>
    /// Callback from llama.cpp with log messages
    /// </summary>
    /// <param name="level"></param>
    /// <param name="message"></param>
	public delegate void LLamaLogCallback(LLamaLogLevel level, string message);

    /// <summary>
    /// Direct translation of the llama.cpp API
    /// </summary>
	public unsafe partial class NativeApi
    {
        static NativeApi()
        {
            // Try to load a preferred library, based on CPU feature detection
            TryLoadLibrary();

            try
            {
                llama_empty_call();
            }
            catch (DllNotFoundException)
            {
                throw new RuntimeError("The native library cannot be found. It could be one of the following reasons: \n" +
                    "1. No LLamaSharp backend was installed. Please search LLamaSharp.Backend and install one of them. \n" +
                    "2. You are using a device with only CPU but installed cuda backend. Please install cpu backend instead. \n" +
                    "3. The backend is not compatible with your system cuda environment. Please check and fix it. If the environment is " +
                    "expected not to be changed, then consider build llama.cpp from source or submit an issue to LLamaSharp.\n" +
                    "4. One of the dependency of the native library is missed.\n");
            }
            llama_backend_init(false);
        }

        /// <summary>
        /// Try to load libllama, using CPU feature detection to try and load a more specialised DLL if possible
        /// </summary>
        /// <returns>The library handle to unload later, or IntPtr.Zero if no library was loaded</returns>
        private static IntPtr TryLoadLibrary()
        {
#if NET6_0_OR_GREATER

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // All of the Windows libraries, in order of preference
                return TryLoad("cu12.1.0/libllama.dll")
                    ?? TryLoad("cu11.7.1/libllama.dll")
#if NET8_0_OR_GREATER
                    ?? TryLoad("avx512/libllama.dll", System.Runtime.Intrinsics.X86.Avx512.IsSupported)
#endif
                    ?? TryLoad("avx2/libllama.dll", System.Runtime.Intrinsics.X86.Avx2.IsSupported)
                    ?? TryLoad("avx/libllama.dll", System.Runtime.Intrinsics.X86.Avx.IsSupported)
                    ?? IntPtr.Zero;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // All of the Linux libraries, in order of preference
                return TryLoad("cu12.1.0/libllama.so")
                    ?? TryLoad("cu11.7.1/libllama.so")
#if NET8_0_OR_GREATER
                    ?? TryLoad("avx512/libllama.so", System.Runtime.Intrinsics.X86.Avx512.IsSupported)
#endif
                    ?? TryLoad("avx2/libllama.so", System.Runtime.Intrinsics.X86.Avx2.IsSupported)
                    ?? TryLoad("avx/libllama.so", System.Runtime.Intrinsics.X86.Avx.IsSupported)
                    ?? IntPtr.Zero;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return IntPtr.Zero;
            }
#endif

            return IntPtr.Zero;

#if NET6_0_OR_GREATER
            // Try to load a DLL from the path if supported. Returns null if nothing is loaded.
            static IntPtr? TryLoad(string path, bool supported = true)
            {
                if (!supported)
                    return null;

                if (NativeLibrary.TryLoad(path, out var handle))
                    return handle;

                return null;
            }
#endif
        }

        private const string libraryName = "libllama";

        /// <summary>
        /// A method that does nothing. This is a native method, calling it will force the llama native dependencies to be loaded.
        /// </summary>
        /// <returns></returns>
        [DllImport(libraryName, EntryPoint = "llama_mmap_supported", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool llama_empty_call();

        /// <summary>
        /// Get the maximum number of devices supported by llama.cpp
        /// </summary>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int llama_max_devices();

        /// <summary>
        /// Create a LLamaModelParams with default values
        /// </summary>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern LLamaModelParams llama_model_default_params();

        /// <summary>
        /// Create a LLamaContextParams with default values
        /// </summary>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern LLamaContextParams llama_context_default_params();

        /// <summary>
        /// Create a LLamaModelQuantizeParams with default values
        /// </summary>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern LLamaModelQuantizeParams llama_model_quantize_default_params();

        /// <summary>
        /// Check if memory mapping is supported
        /// </summary>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool llama_mmap_supported();

        /// <summary>
        /// Check if memory lockingis supported
        /// </summary>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool llama_mlock_supported();

        /// <summary>
        /// Various functions for loading a ggml llama model.
        /// Allocate (almost) all memory needed for the model.
        /// Return NULL on failure
        /// </summary>
        /// <param name="path_model"></param>
        /// <param name="params"></param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr llama_load_model_from_file(string path_model, LLamaModelParams @params);

        /// <summary>
        /// Create a new llama_context with the given model.
        /// Return value should always be wrapped in SafeLLamaContextHandle!
        /// </summary>
        /// <param name="model"></param>
        /// <param name="params"></param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr llama_new_context_with_model(SafeLlamaModelHandle model, LLamaContextParams @params);

        /// <summary>
        /// not great API - very likely to change. 
        /// Initialize the llama + ggml backend
        /// Call once at the start of the program
        /// </summary>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void llama_backend_init(bool numa);

        /// <summary>
        /// Frees all allocated memory in the given llama_context
        /// </summary>
        /// <param name="ctx"></param>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void llama_free(IntPtr ctx);

        /// <summary>
        /// Frees all allocated memory associated with a model
        /// </summary>
        /// <param name="model"></param>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void llama_free_model(IntPtr model);

        /// <summary>
        /// Apply a LoRA adapter to a loaded model
        /// path_base_model is the path to a higher quality model to use as a base for
        /// the layers modified by the adapter. Can be NULL to use the current loaded model.
        /// The model needs to be reloaded before applying a new adapter, otherwise the adapter
        /// will be applied on top of the previous one
        /// </summary>
        /// <param name="model_ptr"></param>
        /// <param name="path_lora"></param>
        /// <param name="scale"></param>
        /// <param name="path_base_model"></param>
        /// <param name="n_threads"></param>
        /// <returns>Returns 0 on success</returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int llama_model_apply_lora_from_file(SafeLlamaModelHandle model_ptr, string path_lora, float scale, string? path_base_model, int n_threads);

        /// <summary>
        /// Sets the current rng seed.
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="seed"></param>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void llama_set_rng_seed(SafeLLamaContextHandle ctx, uint seed);

        /// <summary>
        /// Returns the maximum size in bytes of the state (rng, logits, embedding
        /// and kv_cache) - will often be smaller after compacting tokens
        /// </summary>
        /// <param name="ctx"></param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong llama_get_state_size(SafeLLamaContextHandle ctx);

        /// <summary>
        /// Copies the state to the specified destination address.
        /// Destination needs to have allocated enough memory.
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="dest"></param>
        /// <returns>the number of bytes copied</returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong llama_copy_state_data(SafeLLamaContextHandle ctx, byte* dest);

        /// <summary>
        /// Set the state reading from the specified address
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="src"></param>
        /// <returns>the number of bytes read</returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong llama_set_state_data(SafeLLamaContextHandle ctx, byte* src);

        /// <summary>
        /// Load session file
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="path_session"></param>
        /// <param name="tokens_out"></param>
        /// <param name="n_token_capacity"></param>
        /// <param name="n_token_count_out"></param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool llama_load_session_file(SafeLLamaContextHandle ctx, string path_session, llama_token[] tokens_out, ulong n_token_capacity, ulong* n_token_count_out);

        /// <summary>
        /// Save session file
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="path_session"></param>
        /// <param name="tokens"></param>
        /// <param name="n_token_count"></param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool llama_save_session_file(SafeLLamaContextHandle ctx, string path_session, llama_token[] tokens, ulong n_token_count);

        /// <summary>
        /// Run the llama inference to obtain the logits and probabilities for the next token.
        /// tokens + n_tokens is the provided batch of new tokens to process
        /// n_past is the number of tokens to use from previous eval calls
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="tokens"></param>
        /// <param name="n_tokens"></param>
        /// <param name="n_past"></param>
        /// <returns>Returns 0 on success</returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        [Obsolete("use llama_decode() instead")]
        public static extern int llama_eval(SafeLLamaContextHandle ctx, llama_token* tokens, int n_tokens, int n_past);

        /// <summary>
        /// Convert the provided text into tokens.
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="text"></param>
        /// <param name="encoding"></param>
        /// <param name="tokens"></param>
        /// <param name="n_max_tokens"></param>
        /// <param name="add_bos"></param>
        /// <param name="special">Allow tokenizing special and/or control tokens which otherwise are not exposed and treated as plaintext. Does not insert a leading space.</param>
        /// <returns>Returns the number of tokens on success, no more than n_max_tokens.
        /// Returns a negative number on failure - the number of tokens that would have been returned
        /// </returns>
        public static int llama_tokenize(SafeLLamaContextHandle ctx, string text, Encoding encoding, llama_token[] tokens, int n_max_tokens, bool add_bos, bool special)
        {
            // Calculate number of bytes in text and borrow an array that large (+1 for nul byte)
            var byteCount = encoding.GetByteCount(text);
            var array = ArrayPool<byte>.Shared.Rent(byteCount + 1);
            try
            {
                // Convert to bytes
                fixed (char* textPtr = text)
                fixed (byte* arrayPtr = array)
                {
                    encoding.GetBytes(textPtr, text.Length, arrayPtr, array.Length);
                }

                // Add a zero byte to the end to terminate the string
                array[byteCount] = 0;

                // Do the actual tokenization
                fixed (byte* arrayPtr = array)
                fixed (llama_token* tokensPtr = tokens)
                    return llama_tokenize(ctx.ModelHandle, arrayPtr, byteCount, tokensPtr, n_max_tokens, add_bos, special);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array);
            }
        }

        /// <summary>
        /// Get the size of the context window for the model for this context
        /// </summary>
        /// <param name="ctx"></param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int llama_n_ctx(SafeLLamaContextHandle ctx);

        /// <summary>
        /// Token logits obtained from the last call to llama_eval()
        /// The logits for the last token are stored in the last row
        /// Can be mutated in order to change the probabilities of the next token.<br />
        /// Rows: n_tokens<br />
        /// Cols: n_vocab
        /// </summary>
        /// <param name="ctx"></param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern float* llama_get_logits(SafeLLamaContextHandle ctx);

        /// <summary>
        /// Logits for the ith token. Equivalent to: llama_get_logits(ctx) + i*n_vocab
        /// </summary>
        /// <param name="ctx"></param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern float* llama_get_logits_ith(SafeLLamaContextHandle ctx);

        /// <summary>
        /// Get the embeddings for the input
        /// shape: [n_embd] (1-dimensional)
        /// </summary>
        /// <param name="ctx"></param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern float* llama_get_embeddings(SafeLLamaContextHandle ctx);

        /// <summary>
        /// Get the "Beginning of sentence" token
        /// </summary>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern llama_token llama_token_bos(SafeLLamaContextHandle ctx);

        /// <summary>
        /// Get the "End of sentence" token
        /// </summary>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern llama_token llama_token_eos(SafeLLamaContextHandle ctx);

        /// <summary>
        /// Get the "new line" token
        /// </summary>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern llama_token llama_token_nl(SafeLLamaContextHandle ctx);

        /// <summary>
        /// Print out timing information for this context
        /// </summary>
        /// <param name="ctx"></param>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void llama_print_timings(SafeLLamaContextHandle ctx);

        /// <summary>
        /// Reset all collected timing information for this context
        /// </summary>
        /// <param name="ctx"></param>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void llama_reset_timings(SafeLLamaContextHandle ctx);

        /// <summary>
        /// Print system information
        /// </summary>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr llama_print_system_info();

        /// <summary>
        /// Get the number of tokens in the model vocabulary
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int llama_n_vocab(SafeLlamaModelHandle model);

        /// <summary>
        /// Get the size of the context window for the model
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int llama_n_ctx_train(SafeLlamaModelHandle model);

        /// <summary>
        /// Get the dimension of embedding vectors from this model
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int llama_n_embd(SafeLlamaModelHandle model);

        /// <summary>
        /// Get the size of the model in bytes
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong llama_model_size(SafeLlamaModelHandle model);

        /// <summary>
        /// Get the number of parameters in this model
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong llama_model_n_params(SafeLlamaModelHandle model);

        /// <summary>
        /// Convert a single token into text
        /// </summary>
        /// <param name="model"></param>
        /// <param name="llamaToken"></param>
        /// <param name="buffer">buffer to write string into</param>
        /// <param name="length">size of the buffer</param>
        /// <returns>The length writte, or if the buffer is too small a negative that indicates the length required</returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int llama_token_to_piece(SafeLlamaModelHandle model, int llamaToken, byte* buffer, int length);

        /// <summary>
        /// Convert text into tokens
        /// </summary>
        /// <param name="model"></param>
        /// <param name="text"></param>
        /// <param name="text_len"></param>
        /// <param name="tokens"></param>
        /// <param name="n_max_tokens"></param>
        /// <param name="add_bos"></param>
        /// <param name="special">Allow tokenizing special and/or control tokens which otherwise are not exposed and treated as plaintext. Does not insert a leading space.</param>
        /// <returns>Returns the number of tokens on success, no more than n_max_tokens.
        /// Returns a negative number on failure - the number of tokens that would have been returned
        /// </returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int llama_tokenize(SafeLlamaModelHandle model, byte* text, int text_len, int* tokens, int n_max_tokens, bool add_bos, bool special);

        /// <summary>
        /// Register a callback to receive llama log messages
        /// </summary>
        /// <param name="logCallback"></param>
		[DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
		public static extern void llama_log_set(LLamaLogCallback logCallback);

        /// <summary>
        /// Remove all tokens data of cells in [c0, c1)
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="c0"></param>
        /// <param name="c1"></param>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void llama_kv_cache_tokens_rm(SafeLLamaContextHandle ctx, int c0, int c1);

        /// <summary>
        /// Removes all tokens that belong to the specified sequence and have positions in [p0, p1)
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="seq"></param>
        /// <param name="p0"></param>
        /// <param name="p1"></param>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void llama_kv_cache_seq_rm(SafeLLamaContextHandle ctx, LLamaSeqId seq, LLamaPos p0, LLamaPos p1);

        /// <summary>
        /// Copy all tokens that belong to the specified sequence to another sequence
        /// Note that this does not allocate extra KV cache memory - it simply assigns the tokens to the new sequence
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="src"></param>
        /// <param name="dest"></param>
        /// <param name="p0"></param>
        /// <param name="p1"></param>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void llama_kv_cache_seq_cp(SafeLLamaContextHandle ctx, LLamaSeqId src, LLamaSeqId dest, LLamaPos p0, LLamaPos p1);

        /// <summary>
        /// Removes all tokens that do not belong to the specified sequence
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="seq"></param>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void llama_kv_cache_seq_keep(SafeLLamaContextHandle ctx, LLamaSeqId seq);

        /// <summary>
        /// Adds relative position "delta" to all tokens that belong to the specified sequence and have positions in [p0, p1)
        /// If the KV cache is RoPEd, the KV data is updated accordingly
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="seq"></param>
        /// <param name="p0"></param>
        /// <param name="p1"></param>
        /// <param name="delta"></param>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void llama_kv_cache_seq_shift(SafeLLamaContextHandle ctx, LLamaSeqId seq, LLamaPos p0, LLamaPos p1, LLamaPos delta);

        /// <summary>
        /// Allocates a batch of tokens on the heap
        /// The batch has to be freed with llama_batch_free()
        /// If embd != 0, llama_batch.embd will be allocated with size of n_tokens * embd * sizeof(float)
        /// Otherwise, llama_batch.token will be allocated to store n_tokens llama_token
        /// The rest of the llama_batch members are allocated with size n_tokens
        /// All members are left uninitialized
        /// </summary>
        /// <param name="n_tokens"></param>
        /// <param name="embd"></param>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern LLamaNativeBatch llama_batch_init(int n_tokens, int embd);

        /// <summary>
        /// Frees a batch of tokens allocated with llama_batch_init()
        /// </summary>
        /// <param name="batch"></param>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void llama_batch_free(LLamaNativeBatch batch);

        /// <summary>
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="batch"></param>
        /// <returns>Positive return values does not mean a fatal error, but rather a warning:<br />
        ///  - 0: success<br />
        ///  - 1: could not find a KV slot for the batch (try reducing the size of the batch or increase the context)<br />
        ///  - &lt; 0: error<br />
        /// </returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int llama_decode(SafeLLamaContextHandle ctx, LLamaNativeBatch batch);

        /// <summary>
        /// Set the number of threads used for decoding
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="n_threads">n_threads is the number of threads used for generation (single token)</param>
        /// <param name="n_threads_batch">n_threads_batch is the number of threads used for prompt and batch processing (multiple tokens)</param>
        /// <returns></returns>
        [DllImport(libraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int llama_set_n_threads(SafeLLamaContextHandle ctx, uint n_threads, uint n_threads_batch);
    }
}
