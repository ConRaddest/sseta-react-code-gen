// !!---------------------------------------------------------!!
// !!-------- AUTO-GENERATED: Edit in code generator! --------!!
// !!--------------- CHANGES HERE WILL BE LOST ---------------!!
// !!---------------------------------------------------------!!

import { ApiResponse, FetchRequest, SearchResponse } from "@sseta/components"
import axios, { AxiosInstance, AxiosProgressEvent } from "axios"

import type {
  AUTH_ProfileResponse,

  ADMIN_DepartmentType,
  ADMIN_StaffRoleRequestRetrieveResponse,
  ADMIN_StaffRoleRequest,
  ADMIN_StaffRoleRequestUpdateRequest,

  B_Role,
  B_StaffRoleRequestCreateResponse,
  B_StaffRoleRequestCreateRequest,

  ECD_Role,
  ECD_StaffRoleRequestCreateResponse,
  ECD_StaffRoleRequestCreateRequest,
  ECD_SystemUserUpdateRequest,

  PMVR_Role,
  PMVR_StaffRoleRequestCreateResponse,
  PMVR_StaffRoleRequestCreateRequest,
  PMVR_SystemUserUpdateRequest,

  SP_Role,
  SP_StaffRoleRequestCreateResponse,
  SP_StaffRoleRequestCreateRequest,
  SP_SystemUserUpdateRequest,

  SPI_Role,
  SPI_StaffRoleRequestCreateResponse,
  SPI_StaffRoleRequestCreateRequest,
  SPI_SystemUserUpdateRequest,
} from "../types/management-api.types"

// api client
const Client = (): AxiosInstance => {
  const instance = axios.create({
    baseURL: process.env.NEXT_PUBLIC_API_BASE_URL,
    withCredentials: true,
    headers: {
      "Accept": "application/json",
      "Content-Type": "application/json",
    },
  })

  // Request interceptor to omit undefined and empty string fields
  instance.interceptors.request.use((config) => {
    if (config.data && typeof config.data === "object") {
      config.data = Object.fromEntries(Object.entries(config.data).filter(([_, value]) => value !== undefined && value !== ""))
    }
    return config
  })

  // pushes to logout if the cookie is expired and we are not on an auth endpoint
  instance.interceptors.response.use(
    (success) => {
      return Promise.resolve(success)
    },
    (error) => {
      const isAuthEndpoint = error.config?.url?.includes("/auth/")

      if (error.response?.status === 401 && !isAuthEndpoint) {
        window.location.href = "/logout"
      }
      return Promise.reject(error)
    }
  )

  return instance
}

// Multipart client for file uploads — bypasses the undefined-field interceptor
// so FormData is passed through untouched.
const MultipartClient = (onUploadProgress?: (event: AxiosProgressEvent) => void): AxiosInstance => {
  const instance = axios.create({
    baseURL: process.env.NEXT_PUBLIC_API_BASE_URL,
    withCredentials: true,
    headers: {
      "Accept": "application/json",
      "Content-Type": "multipart/form-data",
    },
    onUploadProgress,
  })

  instance.interceptors.response.use(
    (success) => Promise.resolve(success),
    (error) => {
      const isAuthEndpoint = error.config?.url?.includes("/auth/")

      if (error.response?.status === 401 && !isAuthEndpoint) {
        // window.location.href = "/logout"
      }
      return Promise.reject(error)
    }
  )

  return instance
}

export const ManagementApi = {
  // Auth
  Auth: {
    logout: async (): Promise<ApiResponse<unknown>> => {
      const response = await Client().get("/Auth/Logout")
      return response.data
    },
    profile: async (): Promise<ApiResponse<AUTH_ProfileResponse>> => {
      const response = await Client().get("/Auth/Profile")
      return response.data
    },
  },

  ADMIN: {
    DepartmentType: {
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<ADMIN_DepartmentType>>> => {
          const response = await Client().post(`/management/ADMIN/DepartmentType/Search`, payload)
          return response.data
        },
    },
    StaffRoleRequest: {
        retrieve: async (id: number): Promise<ApiResponse<ADMIN_StaffRoleRequestRetrieveResponse>> => {
          const response = await Client().get(`/management/ADMIN/StaffRoleRequest/Retrieve/${id}`)
          return response.data
        },
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<ADMIN_StaffRoleRequest>>> => {
          const response = await Client().post(`/management/ADMIN/StaffRoleRequest/Search`, payload)
          return response.data
        },
        update: async (id: number, payload: ADMIN_StaffRoleRequestUpdateRequest): Promise<ApiResponse<boolean>> => {
          const response = await Client().put(`/management/ADMIN/StaffRoleRequest/Update/${id}`, payload)
          return response.data
        },
    },
  },

  B: {
    Role: {
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<B_Role>>> => {
          const response = await Client().post(`/management/B/Role/Search`, payload)
          return response.data
        },
    },
    StaffRoleRequest: {
        create: async (payload: B_StaffRoleRequestCreateRequest): Promise<ApiResponse<B_StaffRoleRequestCreateResponse>> => {
          const response = await Client().post(`/management/B/StaffRoleRequest/Create`, payload)
          return response.data
        },
    },
  },

  ECD: {
    Role: {
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<ECD_Role>>> => {
          const response = await Client().post(`/management/ECD/Role/Search`, payload)
          return response.data
        },
    },
    StaffRoleRequest: {
        create: async (payload: ECD_StaffRoleRequestCreateRequest): Promise<ApiResponse<ECD_StaffRoleRequestCreateResponse>> => {
          const response = await Client().post(`/management/ECD/StaffRoleRequest/Create`, payload)
          return response.data
        },
    },
    SystemUser: {
        update: async (id: number, payload: ECD_SystemUserUpdateRequest): Promise<ApiResponse<boolean>> => {
          const response = await Client().put(`/management/ECD/SystemUser/Update/${id}`, payload)
          return response.data
        },
    },
  },

  PMVR: {
    Role: {
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<PMVR_Role>>> => {
          const response = await Client().post(`/management/PMVR/Role/Search`, payload)
          return response.data
        },
    },
    StaffRoleRequest: {
        create: async (payload: PMVR_StaffRoleRequestCreateRequest): Promise<ApiResponse<PMVR_StaffRoleRequestCreateResponse>> => {
          const response = await Client().post(`/management/PMVR/StaffRoleRequest/Create`, payload)
          return response.data
        },
    },
    SystemUser: {
        update: async (id: number, payload: PMVR_SystemUserUpdateRequest): Promise<ApiResponse<boolean>> => {
          const response = await Client().put(`/management/PMVR/SystemUser/Update/${id}`, payload)
          return response.data
        },
    },
  },

  SP: {
    Role: {
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<SP_Role>>> => {
          const response = await Client().post(`/management/SP/Role/Search`, payload)
          return response.data
        },
    },
    StaffRoleRequest: {
        create: async (payload: SP_StaffRoleRequestCreateRequest): Promise<ApiResponse<SP_StaffRoleRequestCreateResponse>> => {
          const response = await Client().post(`/management/SP/StaffRoleRequest/Create`, payload)
          return response.data
        },
    },
    SystemUser: {
        update: async (id: number, payload: SP_SystemUserUpdateRequest): Promise<ApiResponse<boolean>> => {
          const response = await Client().put(`/management/SP/SystemUser/Update/${id}`, payload)
          return response.data
        },
    },
  },

  SPI: {
    Role: {
        search: async (payload: FetchRequest): Promise<ApiResponse<SearchResponse<SPI_Role>>> => {
          const response = await Client().post(`/management/SPI/Role/Search`, payload)
          return response.data
        },
    },
    StaffRoleRequest: {
        create: async (payload: SPI_StaffRoleRequestCreateRequest): Promise<ApiResponse<SPI_StaffRoleRequestCreateResponse>> => {
          const response = await Client().post(`/management/SPI/StaffRoleRequest/Create`, payload)
          return response.data
        },
    },
    SystemUser: {
        update: async (id: number, payload: SPI_SystemUserUpdateRequest): Promise<ApiResponse<boolean>> => {
          const response = await Client().put(`/management/SPI/SystemUser/Update/${id}`, payload)
          return response.data
        },
    },
  },

}
